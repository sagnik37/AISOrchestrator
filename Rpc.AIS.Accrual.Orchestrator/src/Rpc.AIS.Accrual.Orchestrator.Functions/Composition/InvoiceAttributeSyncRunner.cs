using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.InvoiceAttributes;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.InvoiceAttributes;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services;

/// <summary>
/// Builds invoice attribute delta (FS -> FSCM) using Work Order header fields and FSCM snapshots,
/// and injects the computed InvoiceAttributes into the posting payload.
/// 
/// :
/// - This component MUST NOT call FSCM "update" endpoints directly.
/// - Posting pipeline (FscmJournalPoster) is the single place that performs the actual update,
///   and it must happen AFTER successful journal posting (hard dependent).
///
/// Rules:
/// - FS is system-of-record: FS overrides FSCM.
/// - If FS value is null but FSCM has value, AIS clears FSCM (sets null).
/// - Outbound payload uses FSCM attribute names (via Fs->Fscm mapping) OR fixed FSCM names for derived comparisons.
///
/// Mapping source of truth (for standard fields):
/// - FSCM AttributeTypeGlobalAttributes (Name, FSA)
///
/// Option B behavior:
/// - For each (Company, SubProjectId), FSCM definitions + current values are fetched ONCE and cached in-memory for the run.
/// - Comparisons/delta are performed against the in-memory snapshot.
/// </summary>
public sealed partial class InvoiceAttributeSyncRunner
{
    private const string ODataFormattedSuffix = "@OData.Community.Display.V1.FormattedValue";

    // Standard FS keys mapped via IFscmGlobalAttributeMappingClient (FSA -> FSCM schema name).
    // : these are logical FS names (non-underscore), because the mapping table uses those.
    private static readonly string[] FsInvoiceKeys =
    [
        "rpc_wellnametext",
        "rpc_wellnumber",
        "rpc_area",
        "rpc_representativeid",
        "rpc_welllocale",
        "rpc_manufacturingplant",
        "rpc_afe_wbsnumber",
        "rpc_customersignature",
        "rpc_leasename",
        "rpc_ocsgnumber",
        "rpc_rig",
        "rpc_pricelist",
        "rpc_welllatitude",
        "rpc_welllongitude",
        "rpc_countrylookup",
        "rpc_countylookup",
        "rpc_statelookup",
        "rpc_invoicenotesinternal",
        "rpc_declinedtosignreason",
        "rpc_invoicenotesexternal",

        // Used to derive the two fixed FSCM comparisons below.
        "rpc_worktypelookup"
    ];

    // Derived comparisons (fixed FSCM schema names).
    private const string FscmAttr_WorkType = "Work Type";
    private const string FscmAttr_WellAge = "Well Age";
    private const string FscmAttr_TaxabilityType = "Taxability Type";

    private readonly ILogger<InvoiceAttributeSyncRunner> _log;
    private readonly IFsaLineFetcher _fsa;
    private readonly IFscmInvoiceAttributesClient _fscmReadOnly; // read-only usage: definitions + snapshot
    private readonly IFscmGlobalAttributeMappingClient _attrMap;

    public InvoiceAttributeSyncRunner(
        ILogger<InvoiceAttributeSyncRunner> log,
        IFsaLineFetcher fsa,
        IFscmInvoiceAttributesClient fscmReadOnly,
        IFscmGlobalAttributeMappingClient attrMap)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _fsa = fsa ?? throw new ArgumentNullException(nameof(fsa));
        _fscmReadOnly = fscmReadOnly ?? throw new ArgumentNullException(nameof(fscmReadOnly));
        _attrMap = attrMap ?? throw new ArgumentNullException(nameof(attrMap));
    }

    public sealed record EnrichResult(
        bool Attempted,
        bool Success,
        int WorkOrdersWithInvoiceAttributes,
        int TotalAttributePairs,
        string Note,
        string PostingPayloadJson);

    private sealed record WoCtx(Guid WoGuid, string WorkOrderId, string Company, string SubProjectId);

    private sealed record SubProjectKey(string Company, string SubProjectId);

    /// <summary>
    /// Enriches the given posting payload JSON by injecting computed InvoiceAttributes onto each WO element
    /// (only for WOs that have a delta vs FSCM snapshot).
    ///
    /// Orchestrators/endpoints must call FSCM update endpoint AFTER successful journal post using InvoiceAttributesUpdateRunner.
    /// </summary>
    public async Task<EnrichResult> EnrichPostingPayloadAsync(RunContext ctx, string postingPayloadJson, CancellationToken ct)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));

        if (string.IsNullOrWhiteSpace(postingPayloadJson))
            return new EnrichResult(false, true, 0, 0, "Empty payload; invoice attribute enrichment skipped.", postingPayloadJson);

        if (!TryReadWorkOrders(ctx, postingPayloadJson, out var workOrders) || workOrders.Count == 0)
            return new EnrichResult(false, true, 0, 0, "Could not read WorkOrders (Company/SubProjectId/WorkOrderGUID) from payload; invoice attribute enrichment skipped.", postingPayloadJson);

        // Fetch WO headers from Dataverse (once).
        var woGuidStrings = workOrders.Select(w => w.WoGuid.ToString("D")).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        using var woHeadersDoc = await _fsa.GetWorkOrdersAsync(ctx, woGuidStrings, ct).ConfigureAwait(false);

        var woHeaderById = IndexWorkOrderHeaders(woHeadersDoc, _log, ctx);
        if (woHeaderById.Count == 0)
            return new EnrichResult(false, true, 0, 0, "No work order headers returned by Dataverse; invoice attribute enrichment skipped.", postingPayloadJson);

        // Fetch WOP/WOS ONCE (taxability logic).
        using var wopDoc = await _fsa.GetWorkOrderProductsAsync(ctx, woGuidStrings, ct).ConfigureAwait(false);
        using var wosDoc = await _fsa.GetWorkOrderServicesAsync(ctx, woGuidStrings, ct).ConfigureAwait(false);

        var taxabilityByWo = BuildTaxabilityTypeByWorkOrder(wopDoc, wosDoc, _log, ctx);

        // Mapping source of truth for standard keys.
        var mapping = await _attrMap.GetFsToFscmNameMapAsync(ctx, ct).ConfigureAwait(false);
        if (mapping is null || mapping.Count == 0)
        {
            // Keep run alive; we can still proceed with derived fixed FSCM names.
            _log.LogWarning(
                "InvoiceAttributes.Enrich: Fs->Fscm mapping from FSCM is empty. Proceeding with derived-only attributes. RunId={RunId} CorrelationId={CorrelationId}",
                ctx.RunId, ctx.CorrelationId);
            mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        // Option B cache: fetch FSCM data once per subproject for this run.
        var defsCache = new Dictionary<SubProjectKey, HashSet<string>>(capacity: 8);
        var currentCache = new Dictionary<SubProjectKey, Dictionary<string, string?>>(capacity: 8);

        // For each subproject: compute delta once (using first WO as source), then apply to all WOs in that group.
        var groups = workOrders
            .GroupBy(w => new SubProjectKey(w.Company, w.SubProjectId))
            .ToList();

        // Computed invoice updates per WO GUID (in posting payload) => list of pairs to inject.
        var updatesByWoGuid = new Dictionary<Guid, IReadOnlyList<InvoiceAttributePair>>();

        var totalPairs = 0;
        var woWithAttrs = 0;

        foreach (var g in groups)
        {
            ct.ThrowIfCancellationRequested();

            var key = g.Key;
            var source = g.First(); // existing behavior preserved

            if (!woHeaderById.TryGetValue(source.WoGuid, out var woHeader))
            {
                _log.LogWarning(
                    "InvoiceAttributes.Enrich: WO header missing for WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid}.",
                    source.WorkOrderId, source.WoGuid);
                continue;
            }

            // 1) Extract FS attributes (raw FS logical keys).
            var fsAttrsRaw = ExtractFsAttributes(woHeader);

            // 2) Derived comparisons: Work Type + Well Age.
            AddWorkTypeAndWellAgeDerived(woHeader, fsAttrsRaw);

            // 3) Derived comparison: Taxability type (from WOP/WOS).
            if (taxabilityByWo.TryGetValue(source.WoGuid, out var taxability))
                fsAttrsRaw["rpc_taxabilitytype"] = taxability;

            if (fsAttrsRaw.Count == 0)
            {
                _log.LogInformation("InvoiceAttributes.Enrich: No invoice attributes present for WorkOrderId={WorkOrderId}; skipped.", source.WorkOrderId);
                continue;
            }

            // 4) Map FS keys to FSCM schema names.
            var fsAttrsMapped = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in fsAttrsRaw)
            {
                if (kvp.Key.Equals("rpc_taxabilitytype", StringComparison.OrdinalIgnoreCase))
                {
                    fsAttrsMapped[FscmAttr_TaxabilityType] = kvp.Value;
                    continue;
                }

                if (kvp.Key.Equals(FscmAttr_WorkType, StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals(FscmAttr_WellAge, StringComparison.OrdinalIgnoreCase))
                {
                    fsAttrsMapped[kvp.Key] = kvp.Value;
                    continue;
                }

                if (!mapping.TryGetValue(kvp.Key, out var mappedName) || string.IsNullOrWhiteSpace(mappedName))
                    continue;

                fsAttrsMapped[mappedName] = kvp.Value;
            }

            if (fsAttrsMapped.Count == 0)
            {
                _log.LogInformation(
                    "InvoiceAttributes.Enrich: After mapping, no attributes remain for SubProjectId={SubProjectId} WorkOrderId={WorkOrderId}; skipped.",
                    key.SubProjectId, source.WorkOrderId);
                continue;
            }

            // 5) FSCM definitions cached per subproject.
            var active = await GetActiveDefinitionsAsync(ctx, key, defsCache, ct).ConfigureAwait(false);

            // 6) Filter only active attrs (if definitions empty => allow-all).
            var allowed = (active.Count == 0)
                ? new Dictionary<string, string?>(fsAttrsMapped, StringComparer.OrdinalIgnoreCase)
                : fsAttrsMapped
                    .Where(kvp => active.Contains(kvp.Key))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

            if (allowed.Count == 0)
            {
                _log.LogInformation("InvoiceAttributes.Enrich: No mapped attributes are active in FSCM for SubProjectId={SubProjectId}; skipped.", key.SubProjectId);
                continue;
            }

            // 7) FSCM current snapshot cached per subproject.
            var currentDict = await GetCurrentSnapshotAsync(ctx, key, allowed.Keys, currentCache, ct).ConfigureAwait(false);

            // 8) Build delta list.
            var identityMap = allowed.Keys.ToDictionary(k => k, k => k, StringComparer.OrdinalIgnoreCase);
            var delta = InvoiceAttributeDeltaBuilder.BuildDelta(allowed, identityMap, currentDict);

            if (delta.Updates.Count == 0)
            {
                _log.LogInformation("InvoiceAttributes.Enrich: No changes for SubProjectId={SubProjectId}.", key.SubProjectId);
                continue;
            }

            // : Do NOT call FSCM update here.
            // Instead: inject delta.Updates into posting payload (InvoiceAttributes array) for all WOs in this subproject group.
            ApplyUpdatesToCurrentSnapshot(key, delta.Updates, currentCache);

            foreach (var wo in g)
                updatesByWoGuid[wo.WoGuid] = delta.Updates;

            woWithAttrs += g.Count();
            totalPairs += delta.Updates.Count;
        }

        if (updatesByWoGuid.Count == 0)
            return new EnrichResult(true, true, 0, 0, "No invoice attribute changes detected; no enrichment applied.", postingPayloadJson);

        var enrichedJson = InjectInvoiceAttributesIntoPostingPayload(postingPayloadJson, updatesByWoGuid);

        return new EnrichResult(
            Attempted: true,
            Success: true,
            WorkOrdersWithInvoiceAttributes: woWithAttrs,
            TotalAttributePairs: totalPairs,
             "Invoice attributes enriched into posting payload. FSCM update will occur after journal post.",
            PostingPayloadJson: enrichedJson);
    }
}
