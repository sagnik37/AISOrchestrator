// File: .../Core/UseCases/FsaDeltaPayload/FsaDeltaPayloadUseCase.SingleAnyStatus.cs
//
// (FULL FILE CONTENT)
//
// Additive change (Job Operations):
// - Build a WO payload for a single WO GUID even if it is not in the OPEN set.
// - Used by the Cancel Job synchronous HTTP flow to:
//     1) pull all FSA job lines for that WO,
//     2) inject Company/SubProject,
//     3) return the standard WO payload JSON for downstream delta + posting.
//
// : This does NOT change the existing FullFetch behavior.
//
// NOTE (builder):
// - DeltaPayloadBuilder is static; do NOT inject. Use DeltaPayloadBuilder.BuildWoListPayload(...).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload;

namespace Rpc.AIS.Accrual.Orchestrator.Core.UseCases.FsaDeltaPayload;

public sealed partial class FsaDeltaPayloadUseCase
{
    public async Task<GetFsaDeltaPayloadResultDto> BuildSingleWorkOrderAnyStatusAsync(
        GetFsaDeltaPayloadInputDto input,
        FsaDeltaPayloadRunOptions opt,
        CancellationToken ct)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));

        var runId = input.RunId;
        var corr = input.CorrelationId;
        var runContext = new RunContext(runId, DateTimeOffset.UtcNow, input.TriggeredBy, corr);

        if (!Guid.TryParse(input.WorkOrderGuid, out var woGuid) || woGuid == Guid.Empty)
        {
            var emptyPayload = DeltaPayloadBuilder.BuildWoListPayload(Array.Empty<FsaDeltaSnapshot>(), corr, runId);
            return new GetFsaDeltaPayloadResultDto(emptyPayload, null, null, Array.Empty<string>());
        }

        // 1) Fetch the WO header by id (ANY status)
        var woHeaders = await _fetcher
            .GetWorkOrdersAsync(runContext, new List<string> { woGuid.ToString() }, ct)
            .ConfigureAwait(false);

        _telemetry.LogJson("Dataverse.WorkOrder.ById", runId, corr, null, woHeaders.RootElement.GetRawText());

        var woIdToNumber = BuildWorkOrderNumberMap(woHeaders);
        var woIdToCompanyName = FsaDeltaPayloadWorkOrderHeaderMaps.BuildWorkOrderCompanyNameMap(woHeaders);
        var woIdToSubProjectId = FsaDeltaPayloadWorkOrderHeaderMaps.BuildWorkOrderSubProjectIdMap(woHeaders);
        var woIdToHeaderFields = FsaDeltaPayloadWorkOrderHeaderMaps.BuildWorkOrderHeaderFieldsMap(woHeaders);

        if (!woIdToNumber.ContainsKey(woGuid))
        {
            var emptyPayload = DeltaPayloadBuilder.BuildWoListPayload(Array.Empty<FsaDeltaSnapshot>(), corr, runId);
            return new GetFsaDeltaPayloadResultDto(emptyPayload, null, null, Array.Empty<string>());
        }

        // If SubProject is missing, we behave like FullFetch: return empty (caller can treat as 404/business error).
        if (!woIdToSubProjectId.ContainsKey(woGuid))
        {
            var emptyPayload = DeltaPayloadBuilder.BuildWoListPayload(Array.Empty<FsaDeltaSnapshot>(), corr, runId);
            return new GetFsaDeltaPayloadResultDto(emptyPayload, null, null, Array.Empty<string>());
        }

        var woIdStr = woGuid.ToString();

        // 2) Fetch lines for this WO (products + services)
        // Presence check helpers expect strings.
        var hasProducts = (await _fetcher
                .GetWorkOrderIdsWithProductsAsync(runContext, new List<string> { woIdStr }, ct)
                .ConfigureAwait(false))
            .Contains(woIdStr);

        var hasServices = (await _fetcher
                .GetWorkOrderIdsWithServicesAsync(runContext, new List<string> { woIdStr }, ct)
                .ConfigureAwait(false))
            .Contains(woIdStr);

        JsonDocument woProducts = hasProducts
            ? await _fetcher.GetWorkOrderProductsAsync(runContext, new List<string> { woIdStr }, ct).ConfigureAwait(false)
            : JsonDocument.Parse("{\"value\":[]}");

        JsonDocument woServices = hasServices
            ? await _fetcher.GetWorkOrderServicesAsync(runContext, new List<string> { woIdStr }, ct).ConfigureAwait(false)
            : JsonDocument.Parse("{\"value\":[]}");

        _telemetry.LogJson("Dataverse.WO.Products.Single", runId, corr, null, woProducts.RootElement.GetRawText());
        _telemetry.LogJson("Dataverse.WO.Services.Single", runId, corr, null, woServices.RootElement.GetRawText());

        // If no lines exist, return empty payload.
        //if (!hasProducts && !hasServices)
        //{
        //    var emptyPayload = DeltaPayloadBuilder.BuildWoListPayload(Array.Empty<FsaDeltaSnapshot>(), corr, runId);
        //    return new GetFsaDeltaPayloadResultDto(emptyPayload, null, null, Array.Empty<string>());
        //}

        // 3) Product enrichment
        var productIds = new HashSet<Guid>();
        CollectLookupIds(woProducts, productIds, "_msdyn_product_value", "_productid_value");
        CollectLookupIds(woServices, productIds, "_msdyn_service_value", "_msdyn_product_value", "_productid_value");

        var products = await _fetcher.GetProductsAsync(runContext, productIds.ToList(), ct).ConfigureAwait(false);
        _telemetry.LogJson("Dataverse.Products.Single", runId, corr, null, products.RootElement.GetRawText());

        var (productTypeById, itemNumberById) = BuildProductEnrichmentMaps(products);

        // 4) Build snapshot
        var snapshots = _snapshotBuilder.BuildSnapshots(
            impactedWoIds: new List<Guid> { woGuid },
            woNumberById: woIdToNumber,
            woCompanyById: woIdToCompanyName,
            woProducts: woProducts,
            woServices: woServices,
            productTypeById: productTypeById,
            itemNumberById: itemNumberById);

        snapshots = snapshots
            .Select(s => woIdToHeaderFields.TryGetValue(s.WorkOrderId, out var h) ? s with { Header = h } : s)
            .ToList();

        // 4a) Enrich FSCM categories (same behavior as FullFetch)
        snapshots = await EnrichReleasedDistinctProductCategoriesAsync(runContext, snapshots, ct).ConfigureAwait(false);

        // 5) Build outbound payload
        // IMPORTANT: TriggeredBy is not carried on snapshots; pass override so JournalDescription suffix is correct.
        var payloadJson = DeltaPayloadBuilder.BuildWoListPayload(
            snapshots,
            corr,
            runId,
            system: "FieldService",
            triggeredByOverride: input.TriggeredBy);

        // 5a) Inject FS line extras (Currency/Worker/Warehouse/Site/LineNum)
        var extrasByLineGuid = FsaDeltaPayloadLookupMaps.BuildLineExtrasMapForFinalPayload(woProducts, woServices);
        payloadJson = _enricher.InjectFsExtrasAndLogPerWoSummary(payloadJson, extrasByLineGuid, runId, corr);

        // 5b) Inject header-only fields
        payloadJson = _enricher.InjectCompanyIntoPayload(payloadJson, woIdToCompanyName);
        payloadJson = _enricher.InjectSubProjectIdIntoPayload(payloadJson, woIdToSubProjectId);
        // Inject mapping-only WO header fields so downstream delta builder can copy them to final payload.
        payloadJson = _enricher.InjectWorkOrderHeaderFieldsIntoPayload(payloadJson, woIdToHeaderFields);

        // 5c) Stamp journal descriptions using final (post-enrichment) SubProjectId.
        var actionSuffix = DeltaPayloadBuilder.ResolveJournalActionSuffixForTriggeredBy(input.TriggeredBy);
        payloadJson = _enricher.StampJournalDescriptionsIntoPayload(payloadJson, actionSuffix);

        _telemetry.LogJson("Delta.Payload.Outbound.Single", runId, corr, null, payloadJson);

        var woNumbers = snapshots
            .Select(s => s.WorkOrderNumber)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        return new GetFsaDeltaPayloadResultDto(payloadJson, null, null, woNumbers);
    }
}
