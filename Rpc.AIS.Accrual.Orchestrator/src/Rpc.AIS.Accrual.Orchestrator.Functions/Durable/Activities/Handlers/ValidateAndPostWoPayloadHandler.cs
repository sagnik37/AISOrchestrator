using Microsoft.Extensions.Logging;

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Functions.Functions;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using System.Text;
using System.Diagnostics;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services.Handlers;

public sealed partial class ValidateAndPostWoPayloadHandler : ActivitiesHandlerBase
{
    private readonly IPostingClient _posting;
    private readonly FscmODataStagingOptions _odataOpt;
    private readonly IAisLogger _ais;
    private readonly IProcessingAuditService _audit;
    private readonly IWoPayloadCandidateExtractor _candidateExtractor;
    private readonly IPostingAuditWriter _auditWriter;
    private readonly ILogger<ValidateAndPostWoPayloadHandler> _logger;

    public ValidateAndPostWoPayloadHandler(
        IPostingClient posting,
        FscmODataStagingOptions odataOpt,
        IAisLogger ais,
        IProcessingAuditService audit,
        IWoPayloadCandidateExtractor candidateExtractor,
        IPostingAuditWriter auditWriter,
        ILogger<ValidateAndPostWoPayloadHandler> logger)
    {
        _posting = posting ?? throw new ArgumentNullException(nameof(posting));
        _odataOpt = odataOpt ?? throw new ArgumentNullException(nameof(odataOpt));
        _ais = ais ?? throw new ArgumentNullException(nameof(ais));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _candidateExtractor = candidateExtractor ?? throw new ArgumentNullException(nameof(candidateExtractor));
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<PostResult>> HandleAsync(
        DurableAccrualOrchestration.WoPayloadPostingInputDto input,
        RunContext runCtx,
        CancellationToken ct)
    {
        using var scope = BeginScope(_logger, runCtx, "ValidateAndPostWoPayload", input.DurableInstanceId);

        var woPayloadJson = input.WoPayloadJson ?? string.Empty;
        var audit = _audit.CreateSummary(runCtx);

        _logger.LogInformation(
            "Activity ValidateAndPostWoPayload. RunId={RunId} CorrelationId={CorrelationId} Stage={Stage} Outcome={Outcome} PayloadBytes={Bytes}",
            input.RunId, input.CorrelationId, "ValidateAndPost.Begin", TelemetryConventions.Outcomes.Accepted, Encoding.UTF8.GetByteCount(woPayloadJson));

        var candidateWorkOrdersByJournal = _candidateExtractor.ExtractByJournal(woPayloadJson);
        var distinctCandidates = candidateWorkOrdersByJournal
            .Values
            .SelectMany(x => x)
            .GroupBy(x => x.WorkOrderId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.WorkOrderId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        try
        {
            var sw = Stopwatch.StartNew();

            var mode = _odataOpt.Enabled ? "ODataStaging" : "JournalAsync";
            _logger.LogInformation(
                "Activity ValidateAndPostWoPayload: Begin single-validation. Mode={Mode} RunId={RunId} CorrelationId={CorrelationId} Stage={Stage} Outcome={Outcome} CandidateWorkOrders={CandidateCount} WorkOrderCount={WorkOrderCount}",
                mode,
                runCtx.RunId,
                runCtx.CorrelationId,
                "ValidateAndPost.Begin",
                TelemetryConventions.Outcomes.Accepted,
                distinctCandidates.Count,
                distinctCandidates.Count);

            if (IsAdHocAllTrigger(input.TriggeredBy))
            {
                _logger.LogInformation(
                    "ADHOC_ALL_PROGRESS RunId={RunId} CorrelationId={CorrelationId} Stage={Stage} Total={Total} Processed={Processed} Succeeded={Succeeded} Failed={Failed} Remaining={Remaining}",
                    runCtx.RunId,
                    runCtx.CorrelationId,
                    "PostingQueued",
                    distinctCandidates.Count,
                    0,
                    0,
                    0,
                    distinctCandidates.Count);

                foreach (var (candidate, index) in distinctCandidates.Select((value, idx) => (value, idx)))
                {
                    _logger.LogInformation(
                        "ADHOC_ALL_WO_START RunId={RunId} CorrelationId={CorrelationId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid} Index={Index} Total={Total} Stage={Stage}",
                        runCtx.RunId,
                        runCtx.CorrelationId,
                        candidate.WorkOrderId,
                        candidate.WorkOrderGuid,
                        index + 1,
                        distinctCandidates.Count,
                        "ValidateAndPost");
                }
            }

            var results = await _posting.ValidateOnceAndPostAllJournalTypesAsync(runCtx, woPayloadJson, ct);

            sw.Stop();

            var safeResults = results ?? new List<PostResult>();

            _auditWriter.WritePostingAudit(audit, safeResults, candidateWorkOrdersByJournal);
            _audit.LogSummary(audit);

            var failures = safeResults.Count(r => !r.IsSuccess);
            var successes = safeResults.Count - failures;
            var retryableGroups = safeResults.Sum(r => r.RetryableWorkOrders);
            var filteredGroups = safeResults.Sum(r => r.WorkOrdersFiltered);

            if (IsAdHocAllTrigger(input.TriggeredBy))
            {
                var woOutcomes = BuildWorkOrderOutcomes(candidateWorkOrdersByJournal, safeResults);
                var succeededCount = woOutcomes.Count(x => x.IsSuccess);
                var failedCount = woOutcomes.Count - succeededCount;

                foreach (var outcome in woOutcomes)
                {
                    _logger.LogInformation(
                        outcome.IsSuccess
                            ? "ADHOC_ALL_WO_SUCCESS RunId={RunId} CorrelationId={CorrelationId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid} Journals={Journals} Stage={Stage}"
                            : "ADHOC_ALL_WO_FAILED RunId={RunId} CorrelationId={CorrelationId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid} Journals={Journals} Stage={Stage} FailureReason={FailureReason}",
                        runCtx.RunId,
                        runCtx.CorrelationId,
                        outcome.WorkOrderId,
                        outcome.WorkOrderGuid,
                        outcome.JournalsCsv,
                        "ValidateAndPost",
                        outcome.FailureReason);
                }

                _logger.LogInformation(
                    "ADHOC_ALL_PROGRESS RunId={RunId} CorrelationId={CorrelationId} Stage={Stage} Total={Total} Processed={Processed} Succeeded={Succeeded} Failed={Failed} Remaining={Remaining}",
                    runCtx.RunId,
                    runCtx.CorrelationId,
                    "ValidateAndPostCompleted",
                    woOutcomes.Count,
                    woOutcomes.Count,
                    succeededCount,
                    failedCount,
                    0);
            }

            _logger.LogInformation(
                "Activity ValidateAndPostWoPayload: End single-validation. Mode={Mode} ElapsedMs={ElapsedMs} FailureGroups={FailureGroups} SuccessGroups={SuccessGroups} RetryableCount={RetryableCount} FilteredCount={FilteredCount} RunId={RunId} CorrelationId={CorrelationId} Stage={Stage} Outcome={Outcome}",
                mode, sw.ElapsedMilliseconds, failures, successes, retryableGroups, filteredGroups, runCtx.RunId, runCtx.CorrelationId, "ValidateAndPost.End", failures == 0 ? TelemetryConventions.Outcomes.Success : (successes > 0 ? TelemetryConventions.Outcomes.Partial : TelemetryConventions.Outcomes.Failed));

            return safeResults;
        }
        catch (Exception ex)
        {
            var failureCategory = TelemetryConventions.ClassifyFailure(ex);
            _auditWriter.WriteExceptionAudit(audit, ex);
            _audit.LogSummary(audit);

            var err = new PostError(
                Code: "POST_EXCEPTION",
                Message: $"Posting exception (single-validation): {ex.Message}",
                StagingId: null,
                JournalId: null,
                JournalDeleted: false,
                DeleteMessage: ex.ToString());

            _logger.LogError(
                ex,
                "Activity ValidateAndPostWoPayload failed. RunId={RunId} CorrelationId={CorrelationId} Stage={Stage} Outcome={Outcome} FailureCategory={FailureCategory} ErrorType={ErrorType} IsRetryable={IsRetryable}",
                runCtx.RunId,
                runCtx.CorrelationId,
                "ValidateAndPost.End",
                TelemetryConventions.Outcomes.Failed,
                failureCategory,
                ex.GetType().Name,
                false);

            return new List<PostResult>
            {
                new(JournalType.Item, false, null, "Posting threw exception (single-validation).", new[] { err }),
                new(JournalType.Expense, false, null, "Posting threw exception (single-validation).", new[] { err }),
                new(JournalType.Hour, false, null, "Posting threw exception (single-validation).", new[] { err })
            };
        }
    }


    private static bool IsAdHocAllTrigger(string? triggeredBy)
        => string.Equals(triggeredBy, "AdHocAll", StringComparison.OrdinalIgnoreCase)
           || string.Equals(triggeredBy, "AdhocAll", StringComparison.OrdinalIgnoreCase);

    private static List<WorkOrderOutcome> BuildWorkOrderOutcomes(
        IReadOnlyDictionary<JournalType, IReadOnlyList<WoPayloadCandidateWorkOrder>> candidatesByJournal,
        IReadOnlyList<PostResult> results)
    {
        var resultByJournal = results.ToDictionary(r => r.JournalType);
        var map = new Dictionary<string, WorkOrderOutcomeAccumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in candidatesByJournal)
        {
            var journalType = entry.Key;
            resultByJournal.TryGetValue(journalType, out var journalResult);

            foreach (var candidate in entry.Value)
            {
                if (!map.TryGetValue(candidate.WorkOrderId, out var acc))
                {
                    acc = new WorkOrderOutcomeAccumulator(candidate.WorkOrderId, candidate.WorkOrderGuid);
                    map[candidate.WorkOrderId] = acc;
                }

                acc.Journals.Add(journalType.ToString());

                if (journalResult is null || journalResult.IsSuccess)
                {
                    continue;
                }

                acc.IsSuccess = false;
                foreach (var error in journalResult.Errors ?? Array.Empty<PostError>())
                {
                    if (!string.IsNullOrWhiteSpace(error.Message))
                    {
                        acc.FailureReasons.Add(error.Message.Trim());
                    }
                }

                if (acc.FailureReasons.Count == 0 && !string.IsNullOrWhiteSpace(journalResult.SuccessMessage))
                {
                    acc.FailureReasons.Add(journalResult.SuccessMessage.Trim());
                }
            }
        }

        return map.Values
            .OrderBy(x => x.WorkOrderId, StringComparer.OrdinalIgnoreCase)
            .Select(x => new WorkOrderOutcome(
                x.WorkOrderId,
                x.WorkOrderGuid,
                x.IsSuccess,
                string.Join(",", x.Journals.OrderBy(j => j, StringComparer.OrdinalIgnoreCase)),
                x.FailureReasons.Count == 0
                    ? null
                    : string.Join(" | ", x.FailureReasons.Distinct(StringComparer.OrdinalIgnoreCase))))
            .ToList();
    }

    private sealed class WorkOrderOutcomeAccumulator
    {
        public WorkOrderOutcomeAccumulator(string workOrderId, string? workOrderGuid)
        {
            WorkOrderId = workOrderId;
            WorkOrderGuid = workOrderGuid;
        }

        public string WorkOrderId { get; }
        public string? WorkOrderGuid { get; }
        public bool IsSuccess { get; set; } = true;
        public HashSet<string> Journals { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> FailureReasons { get; } = new();
    }

    private sealed record WorkOrderOutcome(
        string WorkOrderId,
        string? WorkOrderGuid,
        bool IsSuccess,
        string JournalsCsv,
        string? FailureReason);

}
