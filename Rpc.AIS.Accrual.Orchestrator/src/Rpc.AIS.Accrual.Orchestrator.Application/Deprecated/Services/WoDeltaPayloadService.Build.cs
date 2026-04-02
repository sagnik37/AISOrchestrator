// File: Rpc.AIS.Accrual.Orchestrator/src/Rpc.AIS.Accrual.Orchestrator.Core/Services/WoDeltaPayloadService.cs


using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata; // DefaultJsonTypeInfoResolver (net8 reflection-disabled)
using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services;

/// <summary>
/// Provides wo delta payload service behavior.
/// </summary>
public sealed partial class WoDeltaPayloadService : IWoDeltaPayloadService, IWoDeltaPayloadServiceV2
{
    private async Task<WoDeltaPayloadBuildResult> BuildDeltaPayloadInternalAsync(
        RunContext context,
        string fsaWoPayloadJson,
        DateTime todayUtc,
        WoDeltaBuildOptions options,
        CancellationToken ct)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (string.IsNullOrWhiteSpace(fsaWoPayloadJson)) throw new ArgumentException("Empty payload.", nameof(fsaWoPayloadJson));

        var root = JsonNode.Parse(fsaWoPayloadJson) ?? throw new InvalidOperationException("Invalid JSON payload.");

        // Option A: CancelToZero forces ALL lines to be treated as inactive so the delta engine emits full reversals.
        if (options.TargetMode == WoDeltaTargetMode.CancelToZero)
        {
            ForceAllLinesInactive(root);
        }

        var woNodes = WoDeltaPayloadClassifier.GetWoList(root);
        var inputWoCount = woNodes.Count;

        await _telemetry.LogPayloadSummaryAsync(
            context,
            step: "Delta",
            message: "FSA payload received (summary).",
            payloadType: "FSA_WO_PAYLOAD",
            workOrderGuid: "MULTI",
            workOrderNumber: null,
            json: fsaWoPayloadJson,
            ct: ct).ConfigureAwait(false);

        // Full payload body logging can be very large for multi-WO runs.
        // Respect AisLogging:LogMultiWoPayloadBody for Timer/Batch scenarios.
        if (_diag.LogPayloadBodies && (_diag.LogMultiWoPayloadBody || inputWoCount <= 1))
        {
            await _telemetry.LogPayloadBodyAsync(
                context,
                step: "Delta",
                message: "FSA payload received",
                payloadType: "FSA_WO_PAYLOAD",
                workOrderGuid: "MULTI",
                workOrderNumber: null,
                json: fsaWoPayloadJson,
                ct: ct).ConfigureAwait(false);
        }

        if (inputWoCount == 0)
            return new WoDeltaPayloadBuildResult(WoDeltaPayloadOutputBuilder.BuildEmptyPayload(), 0, 0, 0, 0, 0);

        var period = await _periodClient.GetSnapshotAsync(context, ct).ConfigureAwait(false);

        int totalDeltaLines = 0, totalReverse = 0, totalRecreate = 0;
        int outputWoCount = 0;

        var outputWoArray = new JsonArray();

        await _aisLogger.InfoAsync(
            context.RunId,
            "Delta",
            "Delta build START.",
            new
            {
                context.CorrelationId,
                WorkOrdersInInput = inputWoCount,
                TodayUtc = todayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            },
            ct).ConfigureAwait(false);

        // -------------------------
        // FSCM journal history fetch (OR-filter batched)
        // - We pre-collect valid WO GUIDs from the payload, then fetch Item/Expense/Hour in chunked batches.
        // - This reduces FSCM calls from (WOs * 3) to (ceil(WOs/chunk) * 3).
        // - We then regroup rows by WorkOrderId for per-WO delta calculation.
        // -------------------------

        var validWos = new List<(JsonObject WoObj, Guid WoGuid, string? WoNumber)>(capacity: inputWoCount);
        var woGuidSet = new HashSet<Guid>();

        foreach (var woNode in woNodes)
        {
            if (woNode is not JsonObject woObj)
                continue;

            var woGuid = WoDeltaPayloadClassifier.GetWorkOrderGuid(woObj);
            var woNumber =
                WoDeltaPayloadClassifier.GetStringLoose(woObj, Keys.WorkOrderID) ??
                WoDeltaPayloadClassifier.GetStringLoose(woObj, "WorkOrderId") ??
                WoDeltaPayloadClassifier.GetStringLoose(woObj, "Work order ID");

            if (woGuid == Guid.Empty)
            {
                await _aisLogger.WarnAsync(
                    context.RunId,
                    "Delta",
                    "Skipping WO because WorkOrderGUID missing/invalid in payload.",
                    new { context.CorrelationId, WorkOrderGuid = (string?)null, WorkOrderNumber = woNumber },
                    ct).ConfigureAwait(false);
                continue;
            }

            validWos.Add((woObj, woGuid, woNumber));
            woGuidSet.Add(woGuid);
        }

        if (validWos.Count == 0)
            return new WoDeltaPayloadBuildResult(WoDeltaPayloadOutputBuilder.BuildEmptyPayload(), 0, 0, 0, 0, 0);

        await _aisLogger.InfoAsync(context.RunId, "Delta", "FSCM journal history fetch (BATCH) START.", new
        {
            context.CorrelationId,
            WorkOrders = validWos.Count,
        }, ct).ConfigureAwait(false);

        var woGuids = woGuidSet.ToList();

        var itemLinesAll = await _fscmFetch.FetchByWorkOrdersAsync(context, JournalType.Item, woGuids, ct).ConfigureAwait(false);
        var expLinesAll = await _fscmFetch.FetchByWorkOrdersAsync(context, JournalType.Expense, woGuids, ct).ConfigureAwait(false);
        var hourLinesAll = await _fscmFetch.FetchByWorkOrdersAsync(context, JournalType.Hour, woGuids, ct).ConfigureAwait(false);

        // Option A: scope FSCM baseline to a specific SubProjectId when requested.
        if (!string.IsNullOrWhiteSpace(options.BaselineSubProjectId))
        {
            var baseline = options.BaselineSubProjectId!.Trim();
            itemLinesAll = FilterBySubProject(itemLinesAll, baseline);
            expLinesAll = FilterBySubProject(expLinesAll, baseline);
            hourLinesAll = FilterBySubProject(hourLinesAll, baseline);
        }


        // Group by WorkOrderId so the existing per-WO aggregator + section builder logic remains unchanged.
        static Dictionary<Guid, List<FscmJournalLine>> GroupByWo(IReadOnlyList<FscmJournalLine> lines)
        {
            var dict = new Dictionary<Guid, List<FscmJournalLine>>();
            foreach (var l in lines)
            {
                if (l.WorkOrderId == Guid.Empty) continue;
                if (!dict.TryGetValue(l.WorkOrderId, out var list))
                    dict[l.WorkOrderId] = list = new List<FscmJournalLine>();
                list.Add(l);
            }
            return dict;
        }

        var itemByWo = GroupByWo(itemLinesAll);
        var expByWo = GroupByWo(expLinesAll);
        var hourByWo = GroupByWo(hourLinesAll);

        await _aisLogger.InfoAsync(context.RunId, "Delta", "FSCM journal history fetch (BATCH) END.", new
        {
            context.CorrelationId,
            ItemLines = itemLinesAll.Count,
            ExpenseLines = expLinesAll.Count,
            HourLines = hourLinesAll.Count
        }, ct).ConfigureAwait(false);

        foreach (var (woObj, woGuid, woNumber) in validWos)
        {
            itemByWo.TryGetValue(woGuid, out var itemLines);
            expByWo.TryGetValue(woGuid, out var expLines);
            hourByWo.TryGetValue(woGuid, out var hourLines);

            var itemAgg = FscmJournalAggregator.GroupByWorkOrderLine(itemLines ?? (IReadOnlyList<FscmJournalLine>)Array.Empty<FscmJournalLine>());
            var expAgg = FscmJournalAggregator.GroupByWorkOrderLine(expLines ?? (IReadOnlyList<FscmJournalLine>)Array.Empty<FscmJournalLine>());
            var hourAgg = FscmJournalAggregator.GroupByWorkOrderLine(hourLines ?? (IReadOnlyList<FscmJournalLine>)Array.Empty<FscmJournalLine>());

            var deltaLinesBeforeThisWo = totalDeltaLines;

            var outWo = new JsonObject();

            // WorkOrderGUID
            if (JsonLooseKey.TryGetNodeLoose(woObj, Keys.WorkOrderGuid, out var woGuidNode) && woGuidNode is not null)
                outWo[Keys.WorkOrderGuid] = woGuidNode.DeepClone();
            else if (JsonLooseKey.TryGetNodeLoose(woObj, "Work order GUID", out woGuidNode) && woGuidNode is not null)
                outWo[Keys.WorkOrderGuid] = woGuidNode.DeepClone();
            else
                outWo[Keys.WorkOrderGuid] = woGuid.ToString("D");

            // WorkOrderID
            if (JsonLooseKey.TryGetNodeLoose(woObj, Keys.WorkOrderID, out var woIdNode) && woIdNode is not null)
                outWo[Keys.WorkOrderID] = woIdNode.DeepClone();
            else if (JsonLooseKey.TryGetNodeLoose(woObj, "WorkOrderId", out woIdNode) && woIdNode is not null)
                outWo[Keys.WorkOrderID] = woIdNode.DeepClone();
            else if (JsonLooseKey.TryGetNodeLoose(woObj, "Work order ID", out woIdNode) && woIdNode is not null)
                outWo[Keys.WorkOrderID] = woIdNode.DeepClone();

            WoDeltaPayloadOutputBuilder.CopyIfPresentLoose(woObj, outWo, Keys.Company);
            WoDeltaPayloadOutputBuilder.CopyIfPresentLoose(woObj, outWo, Keys.SubProjectId);
            if (!outWo.ContainsKey(Keys.SubProjectId))
                WoDeltaPayloadOutputBuilder.CopyIfPresentLoose(woObj, outWo, "SubProjectID");
            // -----------------------------
            // Mapping-only WO header fields (NOT used for delta calc)
            // These are copied through to the output payload so the final DELTA payload
            // has the full header contract for FSCM validation/creation.
            // -----------------------------
            WoDeltaPayloadOutputBuilder.CopyIfPresentLoose(woObj, outWo, "ActualStartDate");
            WoDeltaPayloadOutputBuilder.CopyIfPresentLoose(woObj, outWo, "ActualEndDate");
            WoDeltaPayloadOutputBuilder.CopyIfPresentLoose(woObj, outWo, "ProjectedStartDate");
            WoDeltaPayloadOutputBuilder.CopyIfPresentLoose(woObj, outWo, "ProjectedEndDate");

            WoDeltaPayloadOutputBuilder.CopyIfPresentLoose(woObj, outWo, "Latitude");
            WoDeltaPayloadOutputBuilder.CopyIfPresentLoose(woObj, outWo, "Longitude");

            WoDeltaPayloadOutputBuilder.CopyIfPresentLoose(woObj, outWo, "InvoiceNotesInternal");
            WoDeltaPayloadOutputBuilder.CopyIfPresentLoose(woObj, outWo, "InvoiceNotesExternal");

            WoDeltaPayloadOutputBuilder.CopyIfPresentLoose(woObj, outWo, "FSACustomerReference"); // rpc_ponumber
            WoDeltaPayloadOutputBuilder.CopyIfPresentLoose(woObj, outWo, "FSADeclinedToSign");    // rpc_declinedtosignreason

            // -----------------------------
            // Header fields: copy-through with fallbacks.
            // If callers send minimal WO headers, we still try to populate from alternate keys,
            // and for DimensionDisplayValue we can derive from the first journal line.
            // -----------------------------

            CopyFirstNonEmptyLoose(woObj, outWo, "CountryRegionId",
                "CountryRegionId", "CountryRegionID", "CountryRegion", "Country", "Coountry");

            CopyFirstNonEmptyLoose(woObj, outWo, "County",
                "County", "CountyName");

            CopyFirstNonEmptyLoose(woObj, outWo, "State",
                "State", "StateProvince", "StateOrProvince", "Province");

            // Header DimensionDisplayValue
            CopyFirstNonEmptyLoose(woObj, outWo, "DimensionDisplayValue",
                "DimensionDisplayValue", "DefaultDimensionDisplayValue");

            // If still missing, derive from the first line in any journal section.
            if (!JsonLooseKey.TryGetNodeLoose(outWo, "DimensionDisplayValue", out var ddvNode) || IsNullOrEmptyStringNode(ddvNode))
            {
                var derived = TryDeriveHeaderDimensionDisplayValue(woObj);
                if (!string.IsNullOrWhiteSpace(derived))
                    outWo["DimensionDisplayValue"] = derived;
            }

            CopyFirstNonEmptyLoose(woObj, outWo, "FSATaxabilityType",
                "FSATaxabilityType", "TaxabilityType", "FSA Taxability Type", "rpc_taxabilitytype");

            CopyFirstNonEmptyLoose(woObj, outWo, "FSAWellAge",
                "FSAWellAge", "WellAge", "FSA Well Age", "rpc_wellage");

            CopyFirstNonEmptyLoose(woObj, outWo, "FSAWorkType",
                "FSAWorkType", "WorkType", "FSA Work Type", "rpc_worktype");

            // ----------------------------------------------------
            // Contract hardening:
            // For selected WO header fields, the outbound DELTA payload must always include the key.
            // If the value is missing upstream, emit an empty string (or numeric 0 where appropriate)
            // instead of omitting the property or emitting null.
            // ----------------------------------------------------
            EnsureStringAlways(outWo, "CountryRegionId");
            EnsureStringAlways(outWo, "County");
            EnsureStringAlways(outWo, "State");
            EnsureStringAlways(outWo, "FSAWorkType");
            EnsureStringAlways(outWo, "FSAWellAge");
            EnsureStringAlways(outWo, "FSATaxabilityType");
            EnsureStringAlways(outWo, "FSACustomerReference");
            EnsureStringAlways(outWo, "InvoiceNotesInternal");
            EnsureStringAlways(outWo, "InvoiceNotesExternal");
            EnsureStringAlways(outWo, "FSADeclinedToSign");
            EnsureNumberAlways(outWo, "Latitude", 0m);
            EnsureNumberAlways(outWo, "Longitude", 0m);
            var itemSection = await _sectionBuilder.BuildAsync(
                context,
                woObj,
                new[] { Keys.WoItemLines, "WO Item Lines" },
                Keys.Quantity,
                JournalType.Item,
                woGuid,
                woNumber,
                itemAgg,
                period,
                todayUtc,
                () => totalDeltaLines,
                () => totalDeltaLines++,
                () => totalReverse++,
                () => totalRecreate++,
                ct).ConfigureAwait(false);

            if (itemSection is not null) outWo[Keys.WoItemLines] = itemSection;

            var expSection = await _sectionBuilder.BuildAsync(
                context,
                woObj,
                new[] { Keys.WoExpLines, "WO Exp Lines" },
                Keys.Quantity,
                JournalType.Expense,
                woGuid,
                woNumber,
                expAgg,
                period,
                todayUtc,
                () => totalDeltaLines,
                () => totalDeltaLines++,
                () => totalReverse++,
                () => totalRecreate++,
                ct).ConfigureAwait(false);

            if (expSection is not null) outWo[Keys.WoExpLines] = expSection;

            var hourSection = await _sectionBuilder.BuildAsync(
                context,
                woObj,
                new[] { Keys.WoHourLines, "WO Hour Lines" },
                Keys.Duration,
                JournalType.Hour,
                woGuid,
                woNumber,
                hourAgg,
                period,
                todayUtc,
                () => totalDeltaLines,
                () => totalDeltaLines++,
                () => totalReverse++,
                () => totalRecreate++,
                ct).ConfigureAwait(false);

            if (hourSection is not null) outWo[Keys.WoHourLines] = hourSection;

            if (outWo.ContainsKey(Keys.WoItemLines) || outWo.ContainsKey(Keys.WoExpLines) || outWo.ContainsKey(Keys.WoHourLines))
            {
                outputWoArray.Add(outWo);
                outputWoCount++;

                await _aisLogger.InfoAsync(
                    context.RunId,
                    "Delta",
                    "Delta build WO completed.",
                    new
                    {
                        context.CorrelationId,
                        WorkOrderGuid = woGuid,
                        WorkOrderNumber = woNumber,
                        DeltaLinesAdded = totalDeltaLines - deltaLinesBeforeThisWo,
                        ReverseLinesTotal = totalReverse,
                        RecreateLinesTotal = totalRecreate
                    },
                    ct).ConfigureAwait(false);
            }
            else
            {
                await _aisLogger.InfoAsync(
                    context.RunId,
                    "Delta",
                    "Delta build WO produced no delta lines (skipping output).",
                    new
                    {
                        context.CorrelationId,
                        WorkOrderGuid = woGuid,
                        WorkOrderNumber = woNumber
                    },
                    ct).ConfigureAwait(false);
            }
        }

        var outputRoot = new JsonObject
        {
            [Keys.Request] = new JsonObject
            {
                // DeepClone avoids "node already has a parent" even if this array was previously attached.
                [Keys.WoList] = outputWoArray.DeepClone()
            }
        };

        var jsonOut = outputRoot.ToJsonString(JsonOpts);

        await _telemetry.LogPayloadSummaryAsync(
            context,
            step: "Delta",
            message: "Delta payload built (summary).",
            payloadType: "DELTA_PAYLOAD",
            workOrderGuid: "MULTI",
            workOrderNumber: null,
            json: jsonOut,
            ct: ct).ConfigureAwait(false);

        if (_diag.LogPayloadBodies && _diag.LogMultiWoPayloadBody)
        {
            await _telemetry.LogPayloadBodyAsync(
                context,
                step: "Delta",
                message: "Delta payload built",
                payloadType: "DELTA_PAYLOAD",
                workOrderGuid: "MULTI",
                workOrderNumber: null,
                json: jsonOut,
                ct: ct).ConfigureAwait(false);
        }

        return new WoDeltaPayloadBuildResult(
            DeltaPayloadJson: jsonOut,
            WorkOrdersInInput: inputWoCount,
            WorkOrdersInOutput: outputWoCount,
            TotalDeltaLines: totalDeltaLines,
            TotalReverseLines: totalReverse,
            TotalRecreateLines: totalRecreate
        );
    }



    // -------------------------
    // Option A helpers
    // -------------------------
}
