using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// AdHoc Batch - All Jobs use case.
/// Fully extracted (no shared endpoint handler dependency).
/// </summary>
public sealed class AdHocAllJobsUseCase : JobOperationsUseCaseBase, IAdHocAllJobsUseCase
{
    public AdHocAllJobsUseCase(
        ILogger<AdHocAllJobsUseCase> log,
        IAisLogger aisLogger,
        IAisDiagnosticsOptions diag)
        : base(log, aisLogger, diag)
    {
    }

    public async Task<HttpResponseData> ExecuteAsync(HttpRequestData req, DurableTaskClient client, FunctionContext ctx)
    {
        var body = await ReadBodyAsync(req);
        var (runId, correlationId, _) = ResolveAdHocAllContext(req, body);
        var sourceSystem = "FSCM";

        _log.LogInformation("ADHOC_ALL_REQUEST_ACCEPTED RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} InitiatedBy={InitiatedBy} Stage={Stage} Outcome={Outcome} BodyBytes={BodyBytes}",
            runId, correlationId, sourceSystem, "AdHocAll", "FSCM", "Inbound", TelemetryConventions.Outcomes.Accepted, body?.Length ?? 0);

        using var scope = LogScopes.BeginFunctionScope(_log, new LogScopeContext
        {
            Function = "AdHocBatch_AllJobs",
            Operation = "AdHocBatch_AllJobs",
            FlowName = "AdHocAll",
            Trigger = "Http",
            TriggerName = "AdHocAll",
            TriggerChannel = "Http",
            InitiatedBy = "FSCM",
            RunId = runId,
            CorrelationId = correlationId,
            SourceSystem = sourceSystem
        });

        // EARLY EXIT: FSCM business-event test payload should immediately return 202
        // and must not schedule any durable orchestration.
        if (!string.IsNullOrWhiteSpace(body) && IsTestBusinessEventPayload(body, out var businessEventId))
        {
            _log.LogInformation(
                "AdHocBatch_AllJobs received test business event payload. Returning 202 immediately. BusinessEventId={BusinessEventId} Stage={Stage} Outcome={Outcome}",
                businessEventId,
                "Accepted",
                TelemetryConventions.Outcomes.Skipped);

            return await AcceptedAsync(req, correlationId, runId, new
            {
                runId,
                correlationId,
                sourceSystem,
                operation = "AdHocBatch_AllJobs",
                message = "Test business event received. Request acknowledged; no action was executed.",
                businessEventId
            });
        }

        await LogInboundPayloadAsync(runId, correlationId, "AdHocBatch_AllJobs", body).ConfigureAwait(false);

        var batchId = string.IsNullOrWhiteSpace(runId) ? "adhoc-all" : runId;
        var instanceId = $"{batchId}-adhoc-all";
        var statusQueryRoute = $"/api/adhoc/batch/status?instanceId={instanceId}";

        using var scheduleScope = LogScopes.BeginFunctionScope(_log, new LogScopeContext
        {
            Function = "AdHocBatch_AllJobs",
            Operation = "AdHocBatch_AllJobs",
            FlowName = "AdHocAll",
            Trigger = "Http",
            TriggerName = "AdHocAll",
            TriggerChannel = "Http",
            InitiatedBy = "FSCM",
            Step = "ScheduleDurableOrchestrator",
            Stage = "Accepted",
            RunId = runId,
            CorrelationId = correlationId,
            SourceSystem = sourceSystem,
            BatchId = batchId,
            DurableInstanceId = instanceId
        });

        var existing = await client.GetInstanceAsync(instanceId, ctx.CancellationToken);
        if (existing is not null &&
            (existing.RuntimeStatus == OrchestrationRuntimeStatus.Running ||
             existing.RuntimeStatus == OrchestrationRuntimeStatus.Pending))
        {
            _log.LogWarning(
                "ADHOC_ALL_ALREADY_RUNNING RunId={RunId} CorrelationId={CorrelationId} BatchId={BatchId} InstanceId={InstanceId} RuntimeStatus={RuntimeStatus} SourceSystem={SourceSystem} Stage={Stage} Outcome={Outcome}",
                runId,
                correlationId,
                batchId,
                instanceId,
                existing.RuntimeStatus,
                sourceSystem,
                "Accepted",
                TelemetryConventions.Outcomes.Skipped);

            var existingResponse = new AdHocAllAcceptedResponse(
                Status: "Accepted",
                Message: "An AdHoc bulk run with the same instance id is already active.",
                RunId: runId,
                CorrelationId: correlationId,
                SourceSystem: sourceSystem,
                Trigger: "AdHocAll",
                BatchId: batchId,
                OrchestrationInstanceId: instanceId,
                TrackingMode: "LogsAndDurableStatus",
                RuntimeStatus: existing.RuntimeStatus.ToString(),
                StatusQueryRoute: statusQueryRoute);

            return await AcceptedAsync(req, correlationId, runId, existingResponse);
        }

        var input = new DurableAccrualOrchestration.RunInputDto(
            RunId: runId,
            CorrelationId: correlationId,
            TriggeredBy: "AdHocAll",
            SourceSystem: sourceSystem,
            WorkOrderGuid: null);

        await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(DurableAccrualOrchestration.AccrualOrchestrator),
            input,
            new StartOrchestrationOptions { InstanceId = instanceId },
            ctx.CancellationToken);

        _log.LogInformation(
            "ADHOC_ALL_ACCEPTED RunId={RunId} CorrelationId={CorrelationId} BatchId={BatchId} Orchestrator={Orchestrator} InstanceId={InstanceId} SourceSystem={SourceSystem} TriggerName={TriggerName} InitiatedBy={InitiatedBy} Stage={Stage} Outcome={Outcome} StatusQueryRoute={StatusQueryRoute}",
            runId,
            correlationId,
            batchId,
            nameof(DurableAccrualOrchestration.AccrualOrchestrator),
            instanceId,
            sourceSystem,
            "AdHocAll",
            "FSCM",
            "Accepted",
            TelemetryConventions.Outcomes.Accepted,
            statusQueryRoute);

        var response = new AdHocAllAcceptedResponse(
            Status: "Accepted",
            Message: "AdHoc bulk request accepted for background processing.",
            RunId: runId,
            CorrelationId: correlationId,
            SourceSystem: sourceSystem,
            Trigger: "AdHocAll",
            BatchId: batchId,
            OrchestrationInstanceId: instanceId,
            TrackingMode: "LogsAndDurableStatus",
            RuntimeStatus: "Pending",
            StatusQueryRoute: statusQueryRoute);

        return await AcceptedAsync(req, correlationId, runId, response);
    }
}
