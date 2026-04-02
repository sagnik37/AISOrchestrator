// File: Rpc.AIS.Accrual.Orchestrator/src/Rpc.AIS.Accrual.Orchestrator.Core/Services/DeltaJournalSectionBuilder.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services;

internal sealed partial class DeltaJournalSectionBuilder
{
    private readonly DeltaCalculationEngine _deltaEngine;
    private readonly IAisLogger _aisLogger;

    internal DeltaJournalSectionBuilder(DeltaCalculationEngine deltaEngine, IAisLogger aisLogger)
    {
        _deltaEngine = deltaEngine ?? throw new ArgumentNullException(nameof(deltaEngine));
        _aisLogger = aisLogger ?? throw new ArgumentNullException(nameof(aisLogger));
    }

    internal async Task<JsonObject?> BuildAsync(
        RunContext context,
        JsonObject inputWo,
        string[] journalKeyCandidates,
        string qtyKey,
        JournalType jt,
        Guid woGuid,
        string? woNumber,
        IReadOnlyDictionary<Guid, FscmWorkOrderLineAggregation> aggDict,
        AccountingPeriodSnapshot period,
        DateTime todayUtc,
        Func<int> totalDeltaLines,
        Action incDelta,
        Action incReverse,
        Action incRecreate,
        CancellationToken ct)
    {
        var section = FindFirstObjectLoose(inputWo, journalKeyCandidates);
        if (section is null) return null;

        var journalLines = FindJournalLinesArrayLoose(section);
        if (journalLines is null || journalLines.Count == 0) return null;

        var outLines = new JsonArray();

        foreach (var ln in journalLines)
        {
            if (ln is not JsonObject lineObj) continue;

            aggDict.TryGetValue(GetWorkOrderLineGuid(lineObj), out var fscmAgg);

            var resolved = ResolveDeltaLine(lineObj, qtyKey, jt, woGuid, todayUtc, fscmAgg);
            if (resolved is null) continue;

            var lineId = resolved.WorkOrderLineId;
            var fsa = resolved.Fsa;

            var res = await _deltaEngine.CalculateAsync(
                fsa,
                fscmAgg,
                period,
                todayUtc.Date,
                ct,
                reasonPrefix: $"WO:{jt}");

            await _aisLogger.InfoAsync(
                context.RunId,
                "Delta",
                "Delta decision.",
                new
                {
                    context.CorrelationId,
                    WorkOrderGuid = woGuid,
                    WorkOrderNumber = woNumber,
                    JournalType = jt.ToString(),
                    WorkOrderLineGuid = lineId,
                    Decision = res.Decision.ToString(),
                    Fsa = new
                    {
                        fsa.IsActive,
                        fsa.Quantity,
                        fsa.CalculatedUnitPrice,
                        fsa.LineProperty,
                        fsa.Department,
                        fsa.ProductLine,
                        fsa.Warehouse,
                        fsa.CustomerProductId,
                        fsa.CustomerProductDescription,
                        fsa.TaxabilityType
                    },
                    Fscm = fscmAgg,
                    Planned = res.Lines
                },
                ct).ConfigureAwait(false);

            if (res.Decision == DeltaDecision.NoChange || res.Lines.Count == 0)
                continue;

            foreach (var planned in res.Lines)
            {
                var cloned = await CreatePlannedLineAsync(
                    context,
                    lineObj,
                    resolved,
                    planned,
                    jt,
                    woGuid,
                    woNumber,
                    fscmAgg,
                    period,
                    todayUtc,
                    qtyKey,
                    ct).ConfigureAwait(false);

                outLines.Add(cloned);

                incDelta();
                if (planned.IsReversal) incReverse();
                if (!planned.IsReversal && res.Decision == DeltaDecision.ReverseAndRecreate) incRecreate();
            }
        }

        if (outLines.Count == 0)
            return null;

        var outSection = new JsonObject();

        // 1) JournalDescription FIRST
        CopyIfPresentLoose(section, outSection, "JournalDescription");

        // 2) JournalName SECOND
        // Always preserve JournalName from input section if present.
        // If missing, infer from journal type (safety fallback).
        if (JsonLooseKey.TryGetNodeLoose(section, "JournalName", out var jn) && jn is not null)
        {
            outSection["JournalName"] = jn.DeepClone();
        }
        else
        {
            
            outSection["JournalName"] = "";
        }

        // 3) LineType THIRD
        CopyIfPresentLoose(section, outSection, Keys.LineType);

        // 4) JournalLines LAST
        outSection[Keys.JournalLines] = outLines;

        return outSection;
    }
}
