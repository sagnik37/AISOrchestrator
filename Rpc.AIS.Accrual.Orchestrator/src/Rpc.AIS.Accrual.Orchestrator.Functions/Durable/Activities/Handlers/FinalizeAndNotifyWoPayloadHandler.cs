using Microsoft.Extensions.Logging;

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

using Rpc.AIS.Accrual.Orchestrator.Core.Services;
using Rpc.AIS.Accrual.Orchestrator.Functions.Services;
using Rpc.AIS.Accrual.Orchestrator.Functions.Services.Summaries;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Functions.Functions;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services.Handlers;

public sealed class FinalizeAndNotifyWoPayloadHandler : ActivitiesHandlerBase
{
    private readonly IAisLogger _ais;
    private readonly IEmailSender _email;
    private readonly NotificationOptions _notifications;
    private readonly IWorkOrderProcessingSummaryBuilder _summaryBuilder;
    private readonly ILogger<FinalizeAndNotifyWoPayloadHandler> _logger;

    public FinalizeAndNotifyWoPayloadHandler(
        IAisLogger ais,
        IEmailSender email,
        NotificationOptions notifications,
        IWorkOrderProcessingSummaryBuilder summaryBuilder,
        ILogger<FinalizeAndNotifyWoPayloadHandler> logger)
    {
        _ais = ais ?? throw new ArgumentNullException(nameof(ais));
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _summaryBuilder = summaryBuilder ?? throw new ArgumentNullException(nameof(summaryBuilder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DurableAccrualOrchestration.RunOutcomeDto> HandleAsync(
        DurableAccrualOrchestration.FinalizeWoPayloadInputDto input,
        RunContext runCtx,
        CancellationToken ct)
    {
        using var scope = BeginScope(_logger, runCtx, "FinalizeAndNotifyWoPayload", input.DurableInstanceId);

        _logger.LogInformation(
            "Activity FinalizeAndNotifyWoPayload: Begin. RunId={RunId} CorrelationId={CorrelationId} Stage={Stage} Outcome={Outcome}",
            runCtx.RunId, runCtx.CorrelationId, "Finalize.Begin", TelemetryConventions.Outcomes.Accepted);

        var woConsidered = ActivitiesUseCase.TryGetWorkOrderCount(input.WoPayloadJson ?? string.Empty);

        var postedCounts = input.PostResults.Select(r => r.WorkOrdersPosted).Where(c => c > 0).ToList();
        var woValid = postedCounts.Count > 0 ? postedCounts.Min() : 0;
        var woInvalid = woConsidered > 0 ? Math.Max(0, woConsidered - woValid) : 0;

        var postFailureGroups = input.PostResults.Count(r => !r.IsSuccess);
        var hasGeneralErrors = (input.GeneralErrors?.Length ?? 0) > 0;
        var hasAnyErrors = postFailureGroups > 0 || hasGeneralErrors;

        await _ais.InfoAsync(runCtx.RunId, "End", "Durable accrual orchestration completed (WO payload).", new
        {
            runCtx.CorrelationId,
            WorkOrdersConsidered = woConsidered,
            WorkOrdersValid = woValid,
            WorkOrdersInvalid = woInvalid,
            PostFailureGroups = postFailureGroups,
            GeneralErrors = input.GeneralErrors ?? Array.Empty<string>()
        }, ct);

        LogOperationalSummaryIfNeeded(input, runCtx);

        _logger.LogInformation(
            "FINALIZE_AND_NOTIFY_SUMMARY RunId={RunId} CorrelationId={CorrelationId} Stage={Stage} Outcome={Outcome} WorkOrderCount={WorkOrderCount} SucceededCount={SucceededCount} FailedCount={FailedCount} FailureCategory={FailureCategory}",
            runCtx.RunId,
            runCtx.CorrelationId,
            "Finalize.End",
            hasAnyErrors ? (woValid > 0 ? TelemetryConventions.Outcomes.Partial : TelemetryConventions.Outcomes.Failed) : TelemetryConventions.Outcomes.Success,
            woConsidered,
            woValid,
            woInvalid,
            hasAnyErrors ? TelemetryConventions.FailureCategories.BusinessRule : null);

        if (hasAnyErrors)
        {
            try
            {
                var recipients = _notifications.GetRecipients();
                if (recipients.Count == 0)
                {
                    _logger.LogWarning(
                        "ErrorDistributionList is empty. Failure email will not be sent. RunId={RunId} CorrelationId={CorrelationId}",
                        runCtx.RunId, runCtx.CorrelationId);
                }
                else
                {
                    var subject = $"[AIS][Accrual][ERROR] RunId={runCtx.RunId}";
                    var aggregated = ActivitiesUseCase.AggregateForEmail(input.PostResults);
                    var body = ErrorEmailComposer.ComposeHtml(runCtx, aggregated);
                    await _email.SendAsync(subject, body, recipients, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send failure notification email. RunId={RunId}", runCtx.RunId);
            }
        }

        _logger.LogInformation(
            "Activity FinalizeAndNotifyWoPayload: End. RunId={RunId} CorrelationId={CorrelationId} Stage={Stage} Outcome={Outcome}",
            runCtx.RunId,
            runCtx.CorrelationId,
            "Finalize.End",
            hasAnyErrors ? (woValid > 0 ? TelemetryConventions.Outcomes.Partial : TelemetryConventions.Outcomes.Failed) : TelemetryConventions.Outcomes.Success);

        return new DurableAccrualOrchestration.RunOutcomeDto(
            RunId: runCtx.RunId,
            CorrelationId: runCtx.CorrelationId,
            WorkOrdersConsidered: woConsidered,
            WorkOrdersValid: woValid,
            WorkOrdersInvalid: woInvalid,
            PostFailureGroups: postFailureGroups,
            HasAnyErrors: hasAnyErrors,
            GeneralErrors: (input.GeneralErrors ?? Array.Empty<string>()).ToList());
    }

    private void LogOperationalSummaryIfNeeded(
        DurableAccrualOrchestration.FinalizeWoPayloadInputDto input,
        RunContext runCtx)
    {
        if (!ShouldLogOperationalSummary(runCtx.TriggeredBy))
            return;

        var summary = _summaryBuilder.Build(
            input.WoPayloadJson ?? string.Empty,
            input.PostResults ?? new List<PostResult>(),
            input.GeneralErrors ?? Array.Empty<string>());

        _logger.LogInformation(
            "AIS_WORKORDER_RUN_SUMMARY | RunId={RunId} | CorrelationId={CorrelationId} | TriggeredBy={TriggeredBy} | OpenWorkOrders={OpenWorkOrders} | DataIssueWorkOrders={DataIssueWorkOrders} | ValidatedWorkOrders={ValidatedWorkOrders} | NotValidatedWorkOrders={NotValidatedWorkOrders} | CreatedInFscmSuccessfully={CreatedInFscmSuccessfully} | ErrorWorkOrders={ErrorWorkOrders} | DataIssues={DataIssues} | ErrorDetails={ErrorDetails}",
            runCtx.RunId,
            runCtx.CorrelationId,
            runCtx.TriggeredBy,
            summary.OpenWorkOrders,
            summary.DataIssueWorkOrders,
            summary.ValidatedWorkOrders,
            summary.NotValidatedWorkOrders,
            summary.CreatedInFscmSuccessfully,
            summary.ErrorWorkOrders,
            summary.DataIssues,
            summary.ErrorDetails);
    }

    private static bool ShouldLogOperationalSummary(string? triggeredBy)
        => string.Equals(triggeredBy, "Timer", StringComparison.OrdinalIgnoreCase)
           || string.Equals(triggeredBy, "AdHocAll", StringComparison.OrdinalIgnoreCase)
           || string.Equals(triggeredBy, "AdhocAll", StringComparison.OrdinalIgnoreCase);
}
