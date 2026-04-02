using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Provides durable accrual orchestration behavior.
/// </summary>
public sealed class DurableAccrualOrchestration
{
    private const int AdHocAllWorkOrderChunkSize = 5;

    private readonly ILogger<DurableAccrualOrchestration> _logger;

    public DurableAccrualOrchestration(ILogger<DurableAccrualOrchestration> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // DI-preferred ctor kept for back-compat with existing registrations/tests.
    public DurableAccrualOrchestration(
        ILogger<DurableAccrualOrchestration> logger,
        IOptions<FsOptions> ingestion)
        : this(logger)
    {
    }

    /// <summary>
    /// Carries run input dto data.
    /// </summary>
    public sealed record RunInputDto(string RunId, string CorrelationId, string TriggeredBy, string? SourceSystem = null, string? WorkOrderGuid = null);

    /// <summary>
    /// Carries wo payload posting input dto data.
    /// </summary>
    public sealed record WoPayloadPostingInputDto(string RunId, string CorrelationId, string TriggeredBy, string WoPayloadJson, string? DurableInstanceId = null);

    /// <summary>
    /// Carries retryable payload posting input dto data.
    /// </summary>
    public sealed record RetryableWoPayloadPostingInputDto(
        string RunId,
        string CorrelationId,
        string TriggeredBy,
        string WoPayloadJson,
        JournalType JournalType,
        int Attempt,
        string? DurableInstanceId = null);

    /// <summary>
    /// Carries finalize wo payload input dto data.
    /// </summary>
    public sealed record FinalizeWoPayloadInputDto(
        string RunId,
        string CorrelationId,
        string TriggeredBy,
        string WoPayloadJson,
        List<PostResult> PostResults,
        string[]? GeneralErrors,
        string? DurableInstanceId = null);

    /// <summary>
    /// Carries single wo posting input dto data.
    /// </summary>
    public sealed record SingleWoPostingInputDto(
        string RunId,
        string CorrelationId,
        string TriggeredBy,
        string RawJsonBody,
        string? DurableInstanceId = null);

    /// <summary>
    /// Carries work order status update input dto data.
    /// </summary>
    public sealed record WorkOrderStatusUpdateInputDto(
        string RunId,
        string CorrelationId,
        string TriggeredBy,
        string RawJsonBody,
        string? DurableInstanceId = null);

    /// <summary>
    /// Carries invoice attributes synchronization input dto data (best-effort enrichment/update).
    /// </summary>
    public sealed record InvoiceAttributesSyncInputDto(
        string RunId,
        string CorrelationId,
        string TriggeredBy,
        string WoPayloadJson,
        string? DurableInstanceId = null);

    /// <summary>
    /// Outcome for invoice attributes sync.
    /// </summary>
    public sealed record InvoiceAttributesSyncResultDto(
        bool Attempted,
        bool Success,
        int WorkOrdersWithInvoiceAttributes,
        int TotalAttributePairs,
        string Note,
        int UpdateSuccessCount,
        int UpdateFailureCount);

    /// <summary>
    /// Carries run outcome dto data.
    /// </summary>
    public sealed record RunOutcomeDto(
        string RunId,
        string CorrelationId,
        int WorkOrdersConsidered,
        int WorkOrdersValid,
        int WorkOrdersInvalid,
        int PostFailureGroups,
        bool HasAnyErrors,
        List<string> GeneralErrors)
    {
        /// <summary>
        /// Executes success.
        /// </summary>
        public static RunOutcomeDto Success(string runId, string correlationId)
            => new(runId, correlationId, 0, 0, 0, 0, false, new List<string>());
    }

    private sealed record AdHocAllWorkOrderRef(string WorkOrderId, string WorkOrderGuid);

    public sealed record AdHocAllSingleWorkOrderInputDto(
        string RunId,
        string CorrelationId,
        string TriggeredBy,
        string SourceSystem,
        string BatchId,
        string WorkOrderId,
        string WorkOrderGuid,
        int WorkOrderOrdinal,
        int TotalWorkOrders);

    [Function(nameof(AccrualOrchestrator))]
    public async Task<RunOutcomeDto> AccrualOrchestrator([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<RunInputDto>()
            ?? throw new InvalidOperationException("Orchestration input is required.");

        var instanceId = context.InstanceId;
        var batchId = GetAdHocAllBatchId(input.RunId, instanceId);
        var log = context.CreateReplaySafeLogger(nameof(DurableAccrualOrchestration));

        log.LogInformation(
            "Orchestrator.State.Begin RunId={RunId} CorrelationId={CorrelationId} InstanceId={InstanceId} TriggerName={TriggerName} SourceSystem={SourceSystem} Stage={Stage} WorkOrderGuid={WorkOrderGuid}",
            input.RunId,
            input.CorrelationId,
            instanceId,
            input.TriggeredBy,
            input.SourceSystem,
            "Begin",
            input.WorkOrderGuid);

        if (IsAdHocAllTrigger(input.TriggeredBy))
        {
            return await RunAdHocAllIsolatedAsync(context, input, instanceId, batchId, log);
        }

        return await RunStandardBatchPipelineAsync(context, input, instanceId, batchId, log);
    }

    [Function(nameof(AdHocAllSingleWorkOrderOrchestrator))]
    public async Task<RunOutcomeDto> AdHocAllSingleWorkOrderOrchestrator([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<AdHocAllSingleWorkOrderInputDto>()
            ?? throw new InvalidOperationException("AdHoc All single-work-order orchestration input is required.");

        var instanceId = context.InstanceId;
        var log = context.CreateReplaySafeLogger(nameof(DurableAccrualOrchestration));

        log.LogInformation(
            "ADHOC_ALL_WO_ORCHESTRATOR_BEGIN RunId={RunId} CorrelationId={CorrelationId} BatchId={BatchId} InstanceId={InstanceId} TriggerName={TriggerName} SourceSystem={SourceSystem} Stage={Stage} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid} WorkOrderOrdinal={WorkOrderOrdinal} TotalWorkOrders={TotalWorkOrders}",
            input.RunId,
            input.CorrelationId,
            input.BatchId,
            instanceId,
            input.TriggeredBy,
            input.SourceSystem,
            "Begin",
            input.WorkOrderId,
            input.WorkOrderGuid,
            input.WorkOrderOrdinal,
            input.TotalWorkOrders);

        try
        {
            var delta = await context.CallActivityAsync<GetFsaDeltaPayloadResultDto>(
                nameof(FsaDeltaActivities.GetFsaDeltaPayload),
                new GetFsaDeltaPayloadInputDto(
                    RunId: input.RunId,
                    CorrelationId: input.CorrelationId,
                    TriggeredBy: input.TriggeredBy,
                    WorkOrderGuid: input.WorkOrderGuid,
                    DurableInstanceId: instanceId));

            var originalFsPayloadJson = delta.PayloadJson ?? string.Empty;
            var fetchedSnapshot = CreateWorkOrderSnapshot(originalFsPayloadJson);

            log.LogInformation(
                "ADHOC_ALL_WO_FETCH_COMPLETED RunId={RunId} CorrelationId={CorrelationId} BatchId={BatchId} InstanceId={InstanceId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid} PayloadBytes={PayloadBytes} WorkOrdersFetched={WorkOrdersFetched}",
                input.RunId,
                input.CorrelationId,
                input.BatchId,
                instanceId,
                input.WorkOrderId,
                input.WorkOrderGuid,
                originalFsPayloadJson.Length,
                fetchedSnapshot.TotalCount);

            var deltaResult = await context.CallActivityAsync<BuildDeltaPayloadFromFscmHistoryResultDto>(
                nameof(DeltaActivities.BuildDeltaPayloadFromFscmHistory),
                new BuildDeltaPayloadFromFscmHistoryInputDto(
                    RunId: input.RunId,
                    CorrelationId: input.CorrelationId,
                    TriggeredBy: input.TriggeredBy,
                    FsaPayloadJson: originalFsPayloadJson,
                    DurableInstanceId: instanceId));

            var woPayloadJson = deltaResult.DeltaPayloadJson ?? string.Empty;
            var deltaSnapshot = CreateWorkOrderSnapshot(woPayloadJson);

            log.LogInformation(
                "ADHOC_ALL_WO_DELTA_COMPLETED RunId={RunId} CorrelationId={CorrelationId} BatchId={BatchId} InstanceId={InstanceId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid} DeltaPayloadBytes={DeltaPayloadBytes} WorkOrdersInInput={WorkOrdersInInput} WorkOrdersInOutput={WorkOrdersInOutput} DeltaLines={DeltaLines} ReverseLines={ReverseLines} RecreateLines={RecreateLines}",
                input.RunId,
                input.CorrelationId,
                input.BatchId,
                instanceId,
                input.WorkOrderId,
                input.WorkOrderGuid,
                woPayloadJson.Length,
                deltaResult.WorkOrdersInInput,
                deltaResult.WorkOrdersInOutput,
                deltaResult.DeltaLines,
                deltaResult.ReverseLines,
                deltaResult.RecreateLines);

            var postResults = await context.CallActivityAsync<List<PostResult>>(
                nameof(Activities.ValidateAndPostWoPayload),
                new WoPayloadPostingInputDto(
                    RunId: input.RunId,
                    CorrelationId: input.CorrelationId,
                    TriggeredBy: input.TriggeredBy,
                    WoPayloadJson: woPayloadJson,
                    DurableInstanceId: instanceId)) ?? new List<PostResult>();

            postResults = await RetryRetryableGroupsAsync(context, input.RunId, input.CorrelationId, input.TriggeredBy, instanceId, postResults);

            var outcome = await context.CallActivityAsync<RunOutcomeDto>(
                nameof(Activities.FinalizeAndNotifyWoPayload),
                new FinalizeWoPayloadInputDto(
                    RunId: input.RunId,
                    CorrelationId: input.CorrelationId,
                    TriggeredBy: input.TriggeredBy,
                    WoPayloadJson: woPayloadJson,
                    PostResults: postResults,
                    GeneralErrors: Array.Empty<string>(),
                    DurableInstanceId: instanceId));

            if (postResults.Count == 0 || postResults.All(r => r.IsSuccess))
            {
                await context.CallActivityAsync<InvoiceAttributesSyncResultDto>(
                    nameof(Activities.SyncInvoiceAttributes),
                    new InvoiceAttributesSyncInputDto(input.RunId, input.CorrelationId, input.TriggeredBy, originalFsPayloadJson, instanceId));
            }

            log.LogInformation(
                "ADHOC_ALL_WO_COMPLETED RunId={RunId} CorrelationId={CorrelationId} BatchId={BatchId} InstanceId={InstanceId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid} WorkOrdersConsidered={WorkOrdersConsidered} WorkOrdersValid={WorkOrdersValid} WorkOrdersInvalid={WorkOrdersInvalid} PostFailureGroups={PostFailureGroups} HasAnyErrors={HasAnyErrors} DeltaWorkOrders={DeltaWorkOrders}",
                outcome.RunId,
                outcome.CorrelationId,
                input.BatchId,
                instanceId,
                input.WorkOrderId,
                input.WorkOrderGuid,
                outcome.WorkOrdersConsidered,
                outcome.WorkOrdersValid,
                outcome.WorkOrdersInvalid,
                outcome.PostFailureGroups,
                outcome.HasAnyErrors,
                deltaSnapshot.TotalCount);

            return outcome;
        }
        catch (Exception ex)
        {
            log.LogError(
                ex,
                "ADHOC_ALL_WO_FAILED RunId={RunId} CorrelationId={CorrelationId} BatchId={BatchId} InstanceId={InstanceId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid}",
                input.RunId,
                input.CorrelationId,
                input.BatchId,
                instanceId,
                input.WorkOrderId,
                input.WorkOrderGuid);

            throw;
        }
    }

    private async Task<RunOutcomeDto> RunAdHocAllIsolatedAsync(
        TaskOrchestrationContext context,
        RunInputDto input,
        string instanceId,
        string batchId,
        ILogger log)
    {
        log.LogInformation(
            "ADHOC_ALL_STARTED RunId={RunId} CorrelationId={CorrelationId} BatchId={BatchId} InstanceId={InstanceId} SourceSystem={SourceSystem}",
            input.RunId,
            input.CorrelationId,
            batchId,
            instanceId,
            input.SourceSystem);

        try
        {
            var discovery = await context.CallActivityAsync<GetFsaDeltaPayloadResultDto>(
                nameof(FsaDeltaActivities.GetFsaDeltaPayload),
                new GetFsaDeltaPayloadInputDto(input.RunId, input.CorrelationId, input.TriggeredBy, input.WorkOrderGuid, instanceId));

            var discoveryPayloadJson = discovery.PayloadJson ?? string.Empty;
            var discoveredWorkOrders = CreateWorkOrderRefs(discoveryPayloadJson);
            var discoveredIdsCsv = string.Join(",", discoveredWorkOrders.Select(x => x.WorkOrderId));

            log.LogInformation(
                "ADHOC_ALL_FETCH_COMPLETED RunId={RunId} CorrelationId={CorrelationId} BatchId={BatchId} InstanceId={InstanceId} FsPayloadBytes={FsPayloadBytes} TotalOpenWorkOrders={TotalOpenWorkOrders} WorkOrderIds={WorkOrderIds}",
                input.RunId,
                input.CorrelationId,
                batchId,
                instanceId,
                discoveryPayloadJson.Length,
                discoveredWorkOrders.Count,
                discoveredIdsCsv);

            log.LogInformation(
                "ADHOC_ALL_ISOLATED_EXECUTION_PLAN RunId={RunId} CorrelationId={CorrelationId} BatchId={BatchId} InstanceId={InstanceId} TotalOpenWorkOrders={TotalOpenWorkOrders} ChunkSize={ChunkSize}",
                input.RunId,
                input.CorrelationId,
                batchId,
                instanceId,
                discoveredWorkOrders.Count,
                AdHocAllWorkOrderChunkSize);

            if (discoveredWorkOrders.Count == 0)
            {
                log.LogInformation(
                    "ADHOC_ALL_COMPLETED RunId={RunId} CorrelationId={CorrelationId} BatchId={BatchId} InstanceId={InstanceId} WorkOrdersConsidered=0 WorkOrdersValid=0 WorkOrdersInvalid=0 PostFailureGroups=0 HasAnyErrors=False",
                    input.RunId,
                    input.CorrelationId,
                    batchId,
                    instanceId);

                return RunOutcomeDto.Success(input.RunId, input.CorrelationId);
            }

            var aggregated = RunOutcomeDto.Success(input.RunId, input.CorrelationId);
            var processed = 0;
            var succeeded = 0;
            var failed = 0;
            var chunkNumber = 0;

            foreach (var chunk in Chunk(discoveredWorkOrders, AdHocAllWorkOrderChunkSize))
            {
                chunkNumber++;
                var workOrderChunk = chunk.ToList();
                var childTasks = new List<Task<RunOutcomeDto>>(workOrderChunk.Count);

                foreach (var workOrder in workOrderChunk)
                {
                    var ordinal = processed + childTasks.Count + 1;
                    var childInstanceId = $"{batchId}-wo-{SanitizeForInstanceId(workOrder.WorkOrderId)}";

                    log.LogInformation(
                        "ADHOC_ALL_WO_SCHEDULED RunId={RunId} CorrelationId={CorrelationId} BatchId={BatchId} ParentInstanceId={ParentInstanceId} ChildInstanceId={ChildInstanceId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid} WorkOrderOrdinal={WorkOrderOrdinal} TotalWorkOrders={TotalWorkOrders} ChunkNumber={ChunkNumber}",
                        input.RunId,
                        input.CorrelationId,
                        batchId,
                        instanceId,
                        childInstanceId,
                        workOrder.WorkOrderId,
                        workOrder.WorkOrderGuid,
                        ordinal,
                        discoveredWorkOrders.Count,
                        chunkNumber);

                    childTasks.Add(context.CallSubOrchestratorAsync<RunOutcomeDto>(
                        nameof(AdHocAllSingleWorkOrderOrchestrator),
                        new AdHocAllSingleWorkOrderInputDto(
                            RunId: input.RunId,
                            CorrelationId: input.CorrelationId,
                            TriggeredBy: input.TriggeredBy,
                            SourceSystem: input.SourceSystem ?? string.Empty,
                            BatchId: batchId,
                            WorkOrderId: workOrder.WorkOrderId,
                            WorkOrderGuid: workOrder.WorkOrderGuid,
                            WorkOrderOrdinal: ordinal,
                            TotalWorkOrders: discoveredWorkOrders.Count)));
                }

                var chunkResults = await Task.WhenAll(childTasks);
                processed += chunkResults.Length;
                succeeded += chunkResults.Count(x => !x.HasAnyErrors);
                failed += chunkResults.Count(x => x.HasAnyErrors);

                foreach (var result in chunkResults)
                {
                    aggregated = MergeRunOutcomes(aggregated, result);
                }

                log.LogInformation(
                    "ADHOC_ALL_PROGRESS RunId={RunId} CorrelationId={CorrelationId} BatchId={BatchId} InstanceId={InstanceId} Stage={Stage} ChunkNumber={ChunkNumber} Total={Total} Processed={Processed} Succeeded={Succeeded} Failed={Failed} Remaining={Remaining}",
                    input.RunId,
                    input.CorrelationId,
                    batchId,
                    instanceId,
                    "ChunkCompleted",
                    chunkNumber,
                    discoveredWorkOrders.Count,
                    processed,
                    succeeded,
                    failed,
                    Math.Max(0, discoveredWorkOrders.Count - processed));
            }

            log.LogInformation(
                "ADHOC_ALL_COMPLETED RunId={RunId} CorrelationId={CorrelationId} BatchId={BatchId} InstanceId={InstanceId} WorkOrdersConsidered={WorkOrdersConsidered} WorkOrdersValid={WorkOrdersValid} WorkOrdersInvalid={WorkOrdersInvalid} PostFailureGroups={PostFailureGroups} HasAnyErrors={HasAnyErrors}",
                aggregated.RunId,
                aggregated.CorrelationId,
                batchId,
                instanceId,
                aggregated.WorkOrdersConsidered,
                aggregated.WorkOrdersValid,
                aggregated.WorkOrdersInvalid,
                aggregated.PostFailureGroups,
                aggregated.HasAnyErrors);

            return aggregated;
        }
        catch (Exception ex)
        {
            log.LogError(
                ex,
                "ADHOC_ALL_FAILED RunId={RunId} CorrelationId={CorrelationId} BatchId={BatchId} InstanceId={InstanceId} SourceSystem={SourceSystem}",
                input.RunId,
                input.CorrelationId,
                batchId,
                instanceId,
                input.SourceSystem);

            throw;
        }
    }

    private async Task<RunOutcomeDto> RunStandardBatchPipelineAsync(
        TaskOrchestrationContext context,
        RunInputDto input,
        string instanceId,
        string batchId,
        ILogger log)
    {
        try
        {
            log.LogInformation("Orchestrator.State.FetchFromFsa.Begin RunId={RunId}", input.RunId);

            var delta = await context.CallActivityAsync<GetFsaDeltaPayloadResultDto>(
                nameof(FsaDeltaActivities.GetFsaDeltaPayload),
                new GetFsaDeltaPayloadInputDto(input.RunId, input.CorrelationId, input.TriggeredBy, input.WorkOrderGuid, instanceId));

            log.LogInformation("Orchestrator.State.FetchFromFsa.End RunId={RunId} PayloadBytes={Bytes}", input.RunId, (delta.PayloadJson ?? string.Empty).Length);

            var woPayloadJson = delta.PayloadJson ?? string.Empty;
            var originalFsPayloadJson = woPayloadJson;

            log.LogInformation("Orchestrator.State.FetchFromFscmAndDelta.Begin RunId={RunId}", input.RunId);

            var deltaResult = await context.CallActivityAsync<BuildDeltaPayloadFromFscmHistoryResultDto>(
                nameof(DeltaActivities.BuildDeltaPayloadFromFscmHistory),
                new BuildDeltaPayloadFromFscmHistoryInputDto(
                    RunId: input.RunId,
                    CorrelationId: input.CorrelationId,
                    TriggeredBy: input.TriggeredBy,
                    FsaPayloadJson: woPayloadJson,
                    DurableInstanceId: instanceId));

            woPayloadJson = deltaResult.DeltaPayloadJson ?? string.Empty;

            log.LogInformation("Orchestrator.State.FetchFromFscmAndDelta.End RunId={RunId} PayloadBytes={Bytes}", input.RunId, woPayloadJson.Length);
            log.LogInformation("Orchestrator.State.ValidateAndPost.Begin RunId={RunId}", input.RunId);

            var postResults = await context.CallActivityAsync<List<PostResult>>(
                nameof(Activities.ValidateAndPostWoPayload),
                new WoPayloadPostingInputDto(
                    RunId: input.RunId,
                    CorrelationId: input.CorrelationId,
                    TriggeredBy: input.TriggeredBy,
                    WoPayloadJson: woPayloadJson,
                    DurableInstanceId: instanceId)) ?? new List<PostResult>();

            log.LogInformation("Orchestrator.State.ValidateAndPost.End RunId={RunId} ResultGroups={Groups}", input.RunId, postResults.Count);

            postResults = await RetryRetryableGroupsAsync(context, input.RunId, input.CorrelationId, input.TriggeredBy, instanceId, postResults);

            log.LogInformation("Orchestrator.State.FinalizeAndNotify.Begin RunId={RunId}", input.RunId);

            var outcome = await context.CallActivityAsync<RunOutcomeDto>(
                nameof(Activities.FinalizeAndNotifyWoPayload),
                new FinalizeWoPayloadInputDto(
                    RunId: input.RunId,
                    CorrelationId: input.CorrelationId,
                    TriggeredBy: input.TriggeredBy,
                    WoPayloadJson: woPayloadJson,
                    PostResults: postResults,
                    GeneralErrors: Array.Empty<string>(),
                    DurableInstanceId: instanceId));

            if (postResults.Count == 0 || postResults.All(r => r.IsSuccess))
            {
                await context.CallActivityAsync<InvoiceAttributesSyncResultDto>(
                    nameof(Activities.SyncInvoiceAttributes),
                    new InvoiceAttributesSyncInputDto(input.RunId, input.CorrelationId, input.TriggeredBy, originalFsPayloadJson, instanceId));
            }

            return outcome;
        }
        catch (Exception ex)
        {
            if (IsAdHocAllTrigger(input.TriggeredBy))
            {
                log.LogError(
                    ex,
                    "ADHOC_ALL_FAILED RunId={RunId} CorrelationId={CorrelationId} BatchId={BatchId} InstanceId={InstanceId} SourceSystem={SourceSystem}",
                    input.RunId,
                    input.CorrelationId,
                    batchId,
                    instanceId,
                    input.SourceSystem);
            }

            throw;
        }
    }

    private static string GetAdHocAllBatchId(string? runId, string instanceId)
        => !string.IsNullOrWhiteSpace(runId)
            ? runId
            : (instanceId?.EndsWith("-adhoc-all", StringComparison.OrdinalIgnoreCase) == true
                ? instanceId[..^"-adhoc-all".Length]
                : instanceId);

    private static bool IsAdHocAllTrigger(string? triggeredBy)
        => string.Equals(triggeredBy, "AdHocAll", StringComparison.OrdinalIgnoreCase)
           || string.Equals(triggeredBy, "AdhocAll", StringComparison.OrdinalIgnoreCase);

    private static async Task<List<PostResult>> RetryRetryableGroupsAsync(
        TaskOrchestrationContext context,
        string runId,
        string correlationId,
        string triggeredBy,
        string instanceId,
        List<PostResult> postResults)
    {
        if (postResults is null || postResults.Count == 0)
            return postResults ?? new List<PostResult>();

        const int MaxAttempts = 1;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var retryGroups = postResults
                .Where(r => r.RetryableWorkOrders > 0 && !string.IsNullOrWhiteSpace(r.RetryablePayloadJson))
                .ToList();

            if (retryGroups.Count == 0)
                break;

            foreach (var g in retryGroups)
            {
                var retryResult = await context.CallActivityAsync<PostResult>(
                    nameof(Activities.PostRetryableWoPayload),
                    new RetryableWoPayloadPostingInputDto(
                        RunId: runId,
                        CorrelationId: correlationId,
                        TriggeredBy: triggeredBy,
                        WoPayloadJson: g.RetryablePayloadJson!,
                        JournalType: g.JournalType,
                        Attempt: attempt,
                        DurableInstanceId: instanceId));

                var idx = postResults.FindIndex(x => x.JournalType == g.JournalType);
                if (idx >= 0)
                    postResults[idx] = retryResult;
            }
        }

        return postResults;
    }

    private static WorkOrderSnapshot CreateWorkOrderSnapshot(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return WorkOrderSnapshot.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (!doc.RootElement.TryGetProperty("_request", out var request)
                || !request.TryGetProperty("WOList", out var woList)
                || woList.ValueKind != JsonValueKind.Array)
            {
                return WorkOrderSnapshot.Empty;
            }

            var ids = new List<string>();
            foreach (var item in woList.EnumerateArray())
            {
                if (!item.TryGetProperty("WorkOrderID", out var workOrderIdElement))
                {
                    continue;
                }

                var workOrderId = workOrderIdElement.GetString();
                if (string.IsNullOrWhiteSpace(workOrderId))
                {
                    continue;
                }

                ids.Add(workOrderId.Trim());
            }

            var distinctIds = ids
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new WorkOrderSnapshot(distinctIds.Count, string.Join(",", distinctIds));
        }
        catch
        {
            return WorkOrderSnapshot.Empty;
        }
    }

    private static List<AdHocAllWorkOrderRef> CreateWorkOrderRefs(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new List<AdHocAllWorkOrderRef>();
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (!doc.RootElement.TryGetProperty("_request", out var request)
                || !request.TryGetProperty("WOList", out var woList)
                || woList.ValueKind != JsonValueKind.Array)
            {
                return new List<AdHocAllWorkOrderRef>();
            }

            var workOrders = new List<AdHocAllWorkOrderRef>();
            foreach (var item in woList.EnumerateArray())
            {
                var workOrderId = TryGetStringProperty(item, "WorkOrderID")
                    ?? TryGetStringProperty(item, "WorkOrderId")
                    ?? TryGetStringProperty(item, "WorkOrderNumber");
                var workOrderGuid = TryGetStringProperty(item, "WorkOrderGUID")
                    ?? TryGetStringProperty(item, "WorkOrderGuid")
                    ?? TryGetStringProperty(item, "workOrderGuid");

                if (string.IsNullOrWhiteSpace(workOrderId) || !TryNormalizeGuid(workOrderGuid, out var normalizedGuid))
                {
                    continue;
                }

                workOrders.Add(new AdHocAllWorkOrderRef(workOrderId.Trim(), normalizedGuid));
            }

            return workOrders
                .GroupBy(x => x.WorkOrderId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.WorkOrderId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return new List<AdHocAllWorkOrderRef>();
        }
    }

    private static RunOutcomeDto MergeRunOutcomes(RunOutcomeDto current, RunOutcomeDto next)
        => new(
            RunId: current.RunId,
            CorrelationId: current.CorrelationId,
            WorkOrdersConsidered: current.WorkOrdersConsidered + next.WorkOrdersConsidered,
            WorkOrdersValid: current.WorkOrdersValid + next.WorkOrdersValid,
            WorkOrdersInvalid: current.WorkOrdersInvalid + next.WorkOrdersInvalid,
            PostFailureGroups: current.PostFailureGroups + next.PostFailureGroups,
            HasAnyErrors: current.HasAnyErrors || next.HasAnyErrors,
            GeneralErrors: current.GeneralErrors.Concat(next.GeneralErrors ?? new List<string>()).ToList());

    private static IEnumerable<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> source, int size)
    {
        if (source.Count == 0)
        {
            yield break;
        }

        for (var i = 0; i < source.Count; i += size)
        {
            var count = Math.Min(size, source.Count - i);
            var chunk = new List<T>(count);
            for (var j = 0; j < count; j++)
            {
                chunk.Add(source[i + j]);
            }

            yield return chunk;
        }
    }

    private static string SanitizeForInstanceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var chars = value.Trim()
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')
            .ToArray();

        var sanitized = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static string? TryGetStringProperty(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool TryNormalizeGuid(string? value, out string normalizedGuid)
    {
        normalizedGuid = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim().Trim('{', '}');
        if (!Guid.TryParse(trimmed, out var guid) || guid == Guid.Empty)
        {
            return false;
        }

        normalizedGuid = guid.ToString("D");
        return true;
    }

    private sealed record WorkOrderSnapshot(int TotalCount, string WorkOrderIdsCsv)
    {
        public static WorkOrderSnapshot Empty { get; } = new(0, string.Empty);
    }
}
