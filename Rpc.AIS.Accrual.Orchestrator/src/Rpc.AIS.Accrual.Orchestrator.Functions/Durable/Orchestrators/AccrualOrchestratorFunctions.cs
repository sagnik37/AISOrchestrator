using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using NotificationOptions = Rpc.AIS.Accrual.Orchestrator.Core.Options.NotificationOptions;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;



namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Provides accrual orchestrator functions behavior.
/// </summary>
public sealed class AccrualOrchestratorFunctions
{
    private readonly IRunIdGenerator _ids;
    private readonly ProcessingOptions _processing;
    private readonly NotificationOptions _notifications;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<AccrualOrchestratorFunctions> _logger;

    public AccrualOrchestratorFunctions(
        IRunIdGenerator ids,
        ProcessingOptions processing,
        NotificationOptions notifications,
        IEmailSender emailSender,
        ILogger<AccrualOrchestratorFunctions> logger)
    {
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
        _processing = processing ?? throw new ArgumentNullException(nameof(processing));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _emailSender = emailSender ?? throw new ArgumentNullException(nameof(emailSender));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("AccrualOrchestrator_Timer")]
    public async Task RunTimerAsync(
        [TimerTrigger("%AccrualSchedule%")] TimerInfo timer,
        [DurableClient] DurableTaskClient client,
        FunctionContext ctx)
    {
        var runId = _ids.NewRunId();
        var correlationId = _ids.NewCorrelationId();

        using var scope = LogScopes.BeginFunctionScope(_logger, new LogScopeContext
        {
            Function = "AccrualOrchestrator_Timer",
            Operation = "AccrualOrchestrator_Timer",
            Trigger = "Timer",
            RunId = runId,
            CorrelationId = correlationId,
            SourceSystem = "AIS"
        });

        // Add extra dimensions that are specific to this trigger
        using var extraScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Trigger"] = "Timer",
            ["Mode"] = _processing.Mode
        });
        {
            try
            {
                _logger.LogInformation(
                    "TimerWO.Start RunId={RunId} CorrelationId={CorrelationId} Mode={Mode} ScheduleStatus={ScheduleStatus}",
                    runId,
                    correlationId,
                    _processing.Mode,
                    timer?.ScheduleStatus is null ? "<null>" : "present");

                var recipients = _notifications.GetRecipients();
                if (recipients.Count == 0)
                {
                    _logger.LogWarning("Notifications ErrorDistributionList is empty. Run will proceed without fatal email notifications.");
                }
                else
                {
                    _logger.LogInformation("Notifications configured. RecipientCount={RecipientCount}", recipients.Count);
                }

                // -------------------------------------------------------
                // Schedule Durable Orchestration (single-flight)
                // -------------------------------------------------------
                const string step = "ScheduleDurableOrchestrator";
                var sw = Stopwatch.StartNew();

                _logger.LogInformation("TimerWO.Step.Begin Step={Step}", step);

                // Deterministic instance id enforces single-flight.
                var instanceId = $"Accrual|Timer|{DateTime.UtcNow:yyyyMMddHHmm}";

                var existing = await client.GetInstanceAsync(instanceId, ctx.CancellationToken);
                if (existing is not null &&
                    (existing.RuntimeStatus == OrchestrationRuntimeStatus.Running ||
                     existing.RuntimeStatus == OrchestrationRuntimeStatus.Pending))
                {
                    sw.Stop();

                    _logger.LogWarning(
                        "TimerWO.SingleFlight.Skip InstanceId={InstanceId} ExistingStatus={Status} ElapsedMs={ElapsedMs} RunId={RunId} CorrelationId={CorrelationId}",
                        instanceId,
                        existing.RuntimeStatus,
                        sw.ElapsedMilliseconds,
                        runId,
                        correlationId);

                    _logger.LogInformation("TimerWO.End Status={Status}", "Skipped (single-flight)");
                    return;
                }

                var input = new DurableAccrualOrchestration.RunInputDto(
                    RunId: runId,
                    CorrelationId: correlationId,
                    TriggeredBy: "Timer", SourceSystem: "AIS", WorkOrderGuid: null);

                var options = new StartOrchestrationOptions { InstanceId = instanceId };

                await client.ScheduleNewOrchestrationInstanceAsync(
                    nameof(DurableAccrualOrchestration.AccrualOrchestrator),
                    input,
                    options,
                    ctx.CancellationToken);

                sw.Stop();

                _logger.LogInformation(
                    "TimerWO.Step.End Step={Step} ElapsedMs={ElapsedMs} DurableInstanceId={InstanceId} Orchestrator={OrchestratorName}",
                    step,
                    sw.ElapsedMilliseconds,
                    instanceId,
                    nameof(DurableAccrualOrchestration.AccrualOrchestrator));

                _logger.LogInformation("TimerWO.End Status={Status}", "Completed (scheduled durable)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TimerWO.End Status={Status}", "Failed");

                try
                {
                    var recipients = _notifications.GetRecipients();
                    if (recipients.Count > 0)
                    {
                        var subject = $"[AIS][Accrual][FATAL] RunId={runId}";
                        var body =
                            $"<html><body style='font-family:Segoe UI, Arial'>" +
                            $"<h3>Fatal error in AccrualOrchestrator_Timer</h3>" +
                            $"<p><b>RunId:</b> {WebUtility.HtmlEncode(runId)}<br/>" +
                            $"<b>CorrelationId:</b> {WebUtility.HtmlEncode(correlationId)}</p>" +
                            $"<pre>{WebUtility.HtmlEncode(ex.ToString())}</pre>" +
                            $"</body></html>";

                        await _emailSender.SendAsync(subject, body, recipients, ctx.CancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception emailEx)
                {
                    _logger.LogError(emailEx, "Failed to send fatal error email.");
                }

                throw;
            }
        }
    }
}
