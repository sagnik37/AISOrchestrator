using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Posting workflow coordinating preparation + posting outcome processing.
/// Refactor goal: keep this class focused on orchestration (SRP) and rely on injected pipelines (OCP).
/// </summary>
public sealed class PostingHttpClientWorkflow : IPostingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly JsonSerializerOptions CompactJson = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string RequestKey = WoPayloadJsonToolkit.RequestKey;
    private const string WoListKey = WoPayloadJsonToolkit.WoListKey;
    private const string JournalLinesKey = WoPayloadJsonToolkit.JournalLinesKey;

    // Expected section keys in the WO payload for journal types (existing contract)
    private const string WoItemKey = "WOItemLines";
    private const string WoExpKey = "WOExpLines"; 
    private const string WoHourKey = "WOHourLines";

    private readonly IWoPostingPreparationPipeline _prep;
    private readonly IPostOutcomeProcessor _outcome;
    private readonly ILogger<PostingHttpClientWorkflow> _logger;

    public PostingHttpClientWorkflow(
        IWoPostingPreparationPipeline prep,
        IPostOutcomeProcessor outcome,
        ILogger<PostingHttpClientWorkflow> logger)
    {
        _prep = prep ?? throw new ArgumentNullException(nameof(prep));
        _outcome = outcome ?? throw new ArgumentNullException(nameof(outcome));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PostResult> PostAsync(
        RunContext context,
        JournalType journalType,
        IReadOnlyList<AccrualStagingRef> records,
        CancellationToken ct)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (records is null) throw new ArgumentNullException(nameof(records));

        if (records.Count == 0)
            return new PostResult(journalType, true, null, "No records to post.", Array.Empty<PostError>());

        var payloadJson = BuildJournalAsyncPayloadForRefs(journalType, records);

        var prepared = new PreparedWoPosting(
            JournalType: journalType,
            NormalizedPayloadJson: "{}",
            ProjectedJournalPayloadJson: payloadJson,
            WorkOrdersBefore: 0,
            WorkOrdersAfter: 0,
            RemovedDueToMissingOrEmptySection: 0,
            PreErrors: new List<PostError>(),
            ValidationResponseRaw: null,
            RetryableWorkOrders: 0,
            RetryableLines: 0,
            RetryablePayloadJson: null);

        return await _outcome.PostAndProcessAsync(context, prepared, ct).ConfigureAwait(false);
    }

    public async Task<PostResult> PostFromWoPayloadAsync(
        RunContext context,
        JournalType journalType,
        string woPayloadJson,
        CancellationToken ct)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        if (string.IsNullOrWhiteSpace(woPayloadJson))
            return new PostResult(journalType, true, null, "Empty WO payload. Nothing to post.", Array.Empty<PostError>());

        var prepared = await _prep.PrepareAsync(context, journalType, woPayloadJson, ct).ConfigureAwait(false);
        return await _outcome.PostAndProcessAsync(context, prepared, ct).ConfigureAwait(false);
    }

    public async Task<PostResult> PostValidatedWoPayloadAsync(
        RunContext context,
        JournalType journalType,
        string woPayloadJson,
        IReadOnlyList<PostError> preErrors,
        string? validationResponseRaw,
        CancellationToken ct)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        if (string.IsNullOrWhiteSpace(woPayloadJson))
        {
            return new PostResult(
                journalType,
                true,
                null,
                "Empty WO payload. Nothing to post.",
                Array.Empty<PostError>(),
                ValidationResponseRaw: validationResponseRaw);
        }

        var prepared = await _prep.PrepareValidatedAsync(context, journalType, woPayloadJson, validationResponseRaw, ct).ConfigureAwait(false);

        var merged = new List<PostError>();
        if (preErrors is not null) merged.AddRange(preErrors);
        if (prepared.PreErrors is not null) merged.AddRange(prepared.PreErrors);

        prepared = prepared with { PreErrors = merged };

        return await _outcome.PostAndProcessAsync(context, prepared, ct).ConfigureAwait(false);
    }

    public async Task<List<PostResult>> ValidateOnceAndPostAllJournalTypesAsync(
        RunContext context,
        string woPayloadJson,
        CancellationToken ct)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        if (string.IsNullOrWhiteSpace(woPayloadJson))
            return new List<PostResult>();

        // Preserve existing behavior of "detect types present", but run per-type pipeline (local+remote) for simplicity.
        // This keeps correctness and avoids breaking callers; it may increase validation calls compared to the "validate once" optimization.
        var normalized = WoPayloadJsonToolkit.NormalizeWoPayloadToWoListKey(woPayloadJson);
        var types = WoPayloadJsonToolkit.DetectJournalTypesPresent(normalized, WoItemKey, WoExpKey, WoHourKey).ToList();

        var results = new List<PostResult>();
        foreach (var jt in types)
        {
            _logger.LogInformation(
                "POSTING_PIPELINE_STAGE_BEGIN RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggeredBy={TriggeredBy} Stage={Stage} Outcome={Outcome} JournalType={JournalType}",
                context.RunId,
                context.CorrelationId,
                context.SourceSystem,
                context.TriggeredBy,
                "ValidateAndPost.Begin",
                TelemetryConventions.Outcomes.Accepted,
                jt);

            var r = await PostFromWoPayloadAsync(context, jt, woPayloadJson, ct).ConfigureAwait(false);
            results.Add(r);

            _logger.LogInformation(
                "POSTING_PIPELINE_STAGE_END RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggeredBy={TriggeredBy} Stage={Stage} Outcome={Outcome} JournalType={JournalType} WorkOrderCount={WorkOrderCount} RetryableCount={RetryableCount} ErrorCount={ErrorCount}",
                context.RunId,
                context.CorrelationId,
                context.SourceSystem,
                context.TriggeredBy,
                "ValidateAndPost.End",
                TelemetryConventions.OutcomeFromSuccess(r.IsSuccess),
                jt,
                r.WorkOrdersPosted,
                r.RetryableWorkOrders,
                r.Errors?.Count ?? 0);
        }

        return results;
    }

    private static string BuildJournalAsyncPayloadForRefs(JournalType journalType, IReadOnlyList<AccrualStagingRef> refs)
    {
        var root = new JsonObject
        {
            [RequestKey] = new JsonObject
            {
                ["JournalType"] = journalType.ToString(),
                ["Records"] = JsonSerializer.SerializeToNode(refs, JsonOptions)
            }
        };

        return root.ToJsonString(CompactJson);
    }
}
