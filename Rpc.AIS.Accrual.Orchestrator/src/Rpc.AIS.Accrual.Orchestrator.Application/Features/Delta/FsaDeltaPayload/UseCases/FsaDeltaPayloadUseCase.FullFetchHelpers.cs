using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.UseCases.FsaDeltaPayload;

public sealed partial class FsaDeltaPayloadUseCase
{
    private sealed record EligibleWorkOrderClassification(
        IReadOnlyList<Guid> EligibleWorkOrderIds,
        IReadOnlyList<Guid> IgnoredNoLineWorkOrderIds,
        IReadOnlyCollection<string> WorkOrderIdsWithProducts,
        IReadOnlyCollection<string> WorkOrderIdsWithServices);

    private List<Guid> FilterRequestedOpenWorkOrders(IReadOnlyCollection<Guid> openWoIds, GetFsaDeltaPayloadInputDto input)
    {
        var filtered = openWoIds.ToList();

        if (string.IsNullOrWhiteSpace(input.WorkOrderGuid))
            return filtered;

        if (Guid.TryParse(input.WorkOrderGuid, out var woGuid))
        {
            if (filtered.Contains(woGuid))
            {
                _log.LogInformation("FullFetch SINGLE work order requested. WorkOrderGuid={WorkOrderGuid}", woGuid);
                return new List<Guid> { woGuid };
            }

            _log.LogWarning(
                "FullFetch SINGLE work order requested but not found in OPEN set. WorkOrderGuid={WorkOrderGuid} OpenCount={OpenCount}",
                woGuid, filtered.Count);

            return new List<Guid>();
        }

        _log.LogWarning("FullFetch SINGLE work order requested but invalid GUID supplied. WorkOrderGuid={WorkOrderGuid}", input.WorkOrderGuid);
        return new List<Guid>();
    }

    private List<Guid> ExcludeMissingSubProjectWorkOrders(
        IReadOnlyList<Guid> openWoIds,
        Dictionary<Guid, string> woIdToSubProjectId,
        Dictionary<Guid, string> woIdToNumber,
        Dictionary<Guid, string> woIdToCompanyName,
        string runId,
        string correlationId)
    {
        var invalidNoSubProject = openWoIds
            .Where(id => !woIdToSubProjectId.ContainsKey(id))
            .ToList();

        if (invalidNoSubProject.Count == 0)
            return openWoIds.ToList();

        _log.LogWarning(
            "OPEN work orders missing SubProject will be skipped. InvalidCount={InvalidCount} TotalOpen={TotalOpen} RunId={RunId} CorrelationId={CorrelationId}",
            invalidNoSubProject.Count, openWoIds.Count, runId, correlationId);

        foreach (var id in invalidNoSubProject)
        {
            woIdToNumber.TryGetValue(id, out var woNo);
            woIdToCompanyName.TryGetValue(id, out var company);

            _log.LogWarning(
                "Skipping open work order because SubProject is missing. WorkOrderGuid={WorkOrderGuid} WorkOrderNumber={WorkOrderNumber} Company={Company} RunId={RunId} CorrelationId={CorrelationId}",
                id,
                woNo ?? string.Empty,
                company ?? string.Empty,
                runId,
                correlationId);
        }

        _log.LogInformation(
            "Missing SubProject work orders were logged to telemetry only. Email notification suppressed by design. InvalidCount={InvalidCount} RunId={RunId} CorrelationId={CorrelationId}",
            invalidNoSubProject.Count,
            runId,
            correlationId);

        return openWoIds.Where(id => woIdToSubProjectId.ContainsKey(id)).ToList();
    }

    private async Task<EligibleWorkOrderClassification> ClassifyEligibleWorkOrdersAsync(
        RunContext runContext,
        IReadOnlyList<Guid> openWoIds,
        CancellationToken ct)
    {
        var openWoIdStrings = openWoIds.Select(x => x.ToString()).ToList();

        var woIdsWithProducts = await _fetcher.GetWorkOrderIdsWithProductsAsync(runContext, openWoIdStrings, ct);
        var woIdsWithServices = await _fetcher.GetWorkOrderIdsWithServicesAsync(runContext, openWoIdStrings, ct);

        var eligibleWoIds = new List<Guid>();
        var ignoredNoLines = new List<Guid>();

        foreach (var woId in openWoIds)
        {
            var hasP = woIdsWithProducts.Contains(woId.ToString());
            var hasS = woIdsWithServices.Contains(woId.ToString());

            if (!hasP && !hasS)
            {
                ignoredNoLines.Add(woId);
                continue;
            }

            eligibleWoIds.Add(woId);
        }

        _log.LogWarning(
            "FullFetch classification summary: TotalOpen={TotalOpen} IgnoredNoLines={IgnoredNoLines} Eligible={Eligible}",
            openWoIds.Count, ignoredNoLines.Count, eligibleWoIds.Count);

        return new EligibleWorkOrderClassification(eligibleWoIds, ignoredNoLines, woIdsWithProducts, woIdsWithServices);
    }

    private async Task<(JsonDocument Products, JsonDocument Services)> FetchEligibleLineDocumentsAsync(
        RunContext runContext,
        EligibleWorkOrderClassification classification,
        string runId,
        string correlationId,
        CancellationToken ct)
    {
        JsonDocument woProducts;
        if (classification.WorkOrderIdsWithProducts.Count > 0)
        {
            woProducts = await _fetcher.GetWorkOrderProductsAsync(runContext, classification.WorkOrderIdsWithProducts.ToList(), ct);
            _telemetry.LogJson("Dataverse.WO.Products", runId, correlationId, null, woProducts.RootElement.GetRawText());
        }
        else
        {
            _log.LogWarning("No eligible WOs for product fetch. Skipping products call.");
            woProducts = EmptyValueDocument();
        }

        JsonDocument woServices;
        if (classification.WorkOrderIdsWithServices.Count > 0)
        {
            woServices = await _fetcher.GetWorkOrderServicesAsync(runContext, classification.WorkOrderIdsWithServices.ToList(), ct);
            _telemetry.LogJson("Dataverse.WO.Services", runId, correlationId, null, woServices.RootElement.GetRawText());
        }
        else
        {
            _log.LogWarning("No eligible WOs for service fetch. Skipping services call.");
            woServices = EmptyValueDocument();
        }

        return (woProducts, woServices);
    }
}
