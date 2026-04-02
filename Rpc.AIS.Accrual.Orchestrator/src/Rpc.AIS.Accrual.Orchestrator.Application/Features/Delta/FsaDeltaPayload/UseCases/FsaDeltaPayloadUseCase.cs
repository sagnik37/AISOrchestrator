// File: .../Core/UseCases/FsaDeltaPayload/FsaDeltaPayloadUseCase.cs
//
// (FULL FILE CONTENT)
// Changes applied:
// - Removed DeltaPayloadBuilder injection + field (it is static).
// - Replaced _builder.BuildWoListPayload(...) with DeltaPayloadBuilder.BuildWoListPayload(...).
// - Leave the rest as-is (Core use-case orchestration + thin Functions adapter).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload;
using Rpc.AIS.Accrual.Orchestrator.Application.Features.Delta.FsaDeltaPayload.Services.EnrichmentPipeline;

namespace Rpc.AIS.Accrual.Orchestrator.Core.UseCases.FsaDeltaPayload;

public sealed partial class FsaDeltaPayloadUseCase : IFsaDeltaPayloadUseCase
{
    private readonly ILogger<FsaDeltaPayloadUseCase> _log;
    private readonly ITelemetry _telemetry;
    private readonly IFsaLineFetcher _fetcher;
    private readonly DeltaComparer _comparer;
    private readonly IFscmBaselineFetcher _baseline;
    private readonly IFsaSnapshotBuilder _snapshotBuilder;
    private readonly IFsaDeltaPayloadEnricher _enricher;
    private readonly IFsaDeltaPayloadEnrichmentPipeline _enrichmentPipeline;
    private readonly IFscmReleasedDistinctProductsClient _releasedDistinctProducts;
    private readonly IFscmLegalEntityIntegrationParametersClient _leParams;
    private readonly IEmailSender _email;
    private readonly NotificationOptions _notifications;

    public FsaDeltaPayloadUseCase(
        ILogger<FsaDeltaPayloadUseCase> log,
        ITelemetry telemetry,
        IFsaLineFetcher fetcher,
        DeltaComparer comparer,
        IFscmBaselineFetcher baseline,
        IFsaSnapshotBuilder snapshotBuilder,
        IFsaDeltaPayloadEnricher enricher,
        IFsaDeltaPayloadEnrichmentPipeline enrichmentPipeline,
        IFscmReleasedDistinctProductsClient releasedDistinctProducts,
        IFscmLegalEntityIntegrationParametersClient leParams,
        IEmailSender email,
        NotificationOptions notifications)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
        _baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
        _snapshotBuilder = snapshotBuilder ?? throw new ArgumentNullException(nameof(snapshotBuilder));
        _enricher = enricher ?? throw new ArgumentNullException(nameof(enricher));
        _enrichmentPipeline = enrichmentPipeline ?? throw new ArgumentNullException(nameof(enrichmentPipeline));
        _releasedDistinctProducts = releasedDistinctProducts ?? throw new ArgumentNullException(nameof(releasedDistinctProducts));
        _leParams = leParams ?? throw new ArgumentNullException(nameof(leParams));
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    }

    /// <summary>
    /// Executes build full fetch async.
    /// </summary>
    public Task<GetFsaDeltaPayloadResultDto> BuildFullFetchAsync(GetFsaDeltaPayloadInputDto input, FsaDeltaPayloadRunOptions opt, CancellationToken ct)
        => GetFullFetchPayloadAsync(input, opt, ct);

    private async Task<GetFsaDeltaPayloadResultDto> GetFullFetchPayloadAsync(
            GetFsaDeltaPayloadInputDto input,
            FsaDeltaPayloadRunOptions opt,
            CancellationToken ct)
    {
        var runId = input.RunId;
        var corr = input.CorrelationId;

        var runContext = new RunContext(runId, DateTimeOffset.UtcNow, input.TriggeredBy, corr);

        if (string.IsNullOrWhiteSpace(opt.WorkOrderFilter))
            throw new InvalidOperationException("FsaIngestion:WorkOrderFilter is required for FullFetch mode.");

        _log.LogInformation("FullFetch START. WorkOrderFilter={Filter}", opt.WorkOrderFilter);

        // 1) Fetch OPEN work orders (headers)
        var openWoHeaders = await _fetcher.GetOpenWorkOrdersAsync(runContext, ct);
        _telemetry.LogJson("Dataverse.OpenWorkOrders", runId, corr, null, openWoHeaders.RootElement.GetRawText());

        var woIdToNumber = BuildWorkOrderNumberMap(openWoHeaders);
        var woIdToCompanyName = FsaDeltaPayloadWorkOrderHeaderMaps.BuildWorkOrderCompanyNameMap(openWoHeaders);
        var woIdToSubProjectId = FsaDeltaPayloadWorkOrderHeaderMaps.BuildWorkOrderSubProjectIdMap(openWoHeaders);
        var woIdToHeaderFields = FsaDeltaPayloadWorkOrderHeaderMaps.BuildWorkOrderHeaderFieldsMap(openWoHeaders);

        var openWoIds = FilterRequestedOpenWorkOrders(woIdToNumber.Keys.ToList(), input);

        _log.LogInformation(
            "FullFetch OPEN work orders fetched. Count={Count} WithCompany={WithCompany} WithSubProject={WithSubProject}",
            openWoIds.Count,
            woIdToCompanyName.Count,
            woIdToSubProjectId.Count);

        openWoIds = ExcludeMissingSubProjectWorkOrders(
            openWoIds,
            woIdToSubProjectId,
            woIdToNumber,
            woIdToCompanyName,
            runId,
            corr);
        if (openWoIds.Count == 0)
        {
            var emptyPayload = DeltaPayloadBuilder.BuildWoListPayload(Array.Empty<FsaDeltaSnapshot>(), corr, runId);
            _telemetry.LogJson("Delta.Payload.Outbound", runId, corr, null, emptyPayload);

            _log.LogInformation("FullFetch END. No open work orders.");

            return new GetFsaDeltaPayloadResultDto(
                PayloadJson: emptyPayload,
                ProductDeltaLinkAfter: null,
                ServiceDeltaLinkAfter: null,
                WorkOrderNumbers: Array.Empty<string>());
        }

        // 2) Presence checks first (cheap)
        var classification = await ClassifyEligibleWorkOrdersAsync(runContext, openWoIds, ct).ConfigureAwait(false);
        var eligibleWoIds = classification.EligibleWorkOrderIds;
        var ignoredNoLines = classification.IgnoredNoLineWorkOrderIds;

        if (eligibleWoIds.Count == 0)
        {
            var minimalPayload = BuildHeaderOnlyWoListPayload(openWoIds, woIdToNumber, woIdToCompanyName, woIdToSubProjectId, corr, runId);
            _telemetry.LogJson("Delta.Payload.Outbound", runId, corr, null, minimalPayload);

            _log.LogWarning("FullFetch END. All open work orders were ignored due to no line items. Returning header-only payload so invoice attributes can still sync.");

            return new GetFsaDeltaPayloadResultDto(
                PayloadJson: minimalPayload,
                ProductDeltaLinkAfter: null,
                ServiceDeltaLinkAfter: null,
                WorkOrderNumbers: openWoIds.Where(id => woIdToNumber.ContainsKey(id)).Select(id => woIdToNumber[id]!).ToArray());
        }

        // 3) Fetch only the required line types.
        var (woProducts, woServices) = await FetchEligibleLineDocumentsAsync(
            runContext,
            classification,
            runId,
            corr,
            ct).ConfigureAwait(false);

        // 4) Two-phase product enrichment
        var productIds = new HashSet<Guid>();
        CollectLookupIds(woProducts, productIds, "_msdyn_product_value", "_productid_value");
        CollectLookupIds(woServices, productIds, "_msdyn_service_value", "_msdyn_product_value", "_productid_value");

        var products = await _fetcher.GetProductsAsync(runContext, productIds.ToList(), ct);
        _telemetry.LogJson("Dataverse.Products", runId, corr, null, products.RootElement.GetRawText());

        var (productTypeById, itemNumberById) = BuildProductEnrichmentMaps(products);

        // 5) Build snapshots (only for eligible WOs)
        var snapshots = _snapshotBuilder.BuildSnapshots(
    impactedWoIds: eligibleWoIds,
    woNumberById: woIdToNumber,
    woCompanyById: woIdToCompanyName,
    woProducts: woProducts,
    woServices: woServices,
    productTypeById: productTypeById,
    itemNumberById: itemNumberById);

        snapshots = snapshots
            .Select(s => woIdToHeaderFields.TryGetValue(s.WorkOrderId, out var h) ? s with { Header = h } : s)
            .ToList();

        // 5a) Enrich Project Categories from FSCM CDSReleasedDistinctProducts (Dataverse category not used)
        snapshots = await EnrichReleasedDistinctProductCategoriesAsync(runContext, snapshots, ct).ConfigureAwait(false);

        // 6) Outbound payload
        // IMPORTANT: TriggeredBy is not carried on snapshots; pass override so JournalDescription suffix is correct.
        var payloadJson = DeltaPayloadBuilder.BuildWoListPayload(
            snapshots,
            corr,
            runId,
            system: "FieldService",
            triggeredByOverride: input.TriggeredBy);

        // 6a) Enrich payload via step-per-concern pipeline (OCP-friendly)
        var extrasByLineGuid = FsaDeltaPayloadLookupMaps.BuildLineExtrasMapForFinalPayload(woProducts, woServices);
        var journalNamesByCompany = await FetchJournalNamesByCompanyAsync(runContext, woIdToCompanyName, ct).ConfigureAwait(false);

        var actionSuffix = DeltaPayloadBuilder.ResolveJournalActionSuffixForTriggeredBy(input.TriggeredBy);

        var enrichmentCtx = new EnrichmentContext(
            PayloadJson: payloadJson,
            RunId: runId,
            CorrelationId: corr,
            Action: actionSuffix,
            ExtrasByLineGuid: extrasByLineGuid,
            WoIdToCompanyName: woIdToCompanyName,
            JournalNamesByCompany: journalNamesByCompany,
            WoIdToSubProjectId: woIdToSubProjectId,
            WoIdToHeaderFields: woIdToHeaderFields);

        payloadJson = await _enrichmentPipeline.ApplyAsync(enrichmentCtx, ct).ConfigureAwait(false);

        _telemetry.LogJson("Delta.Payload.Outbound", runId, corr, null, payloadJson);

        // 7) FSCM baseline scaffold (unchanged)
        var baselineRecords = await _baseline.FetchBaselineAsync(ct);
        _log.LogInformation("FSCM baseline fetched (scaffold only). RecordCount={Count}", baselineRecords?.Count ?? 0);

        var woNumbers = snapshots.Select(s => s.WorkOrderNumber).Distinct().ToList();
        _log.LogInformation("FullFetch END WorkOrders={Count}", woNumbers.Count);

        return new GetFsaDeltaPayloadResultDto(
            PayloadJson: payloadJson,
            ProductDeltaLinkAfter: null,
            ServiceDeltaLinkAfter: null,
            WorkOrderNumbers: woNumbers);
    }

    private async Task<IReadOnlyDictionary<string, LegalEntityJournalNames>> FetchJournalNamesByCompanyAsync(
        RunContext ctx,
        IReadOnlyDictionary<Guid, string> woIdToCompanyName,
        CancellationToken ct)
    {
        var result = new Dictionary<string, LegalEntityJournalNames>(StringComparer.OrdinalIgnoreCase);

        if (woIdToCompanyName is null || woIdToCompanyName.Count == 0)
            return result;

        foreach (var company in woIdToCompanyName.Values
                     .Where(c => !string.IsNullOrWhiteSpace(c))
                     .Select(c => c.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (result.ContainsKey(company))
                continue;

            try
            {
                var names = await _leParams.GetJournalNamesAsync(ctx, company, ct).ConfigureAwait(false);
                result[company] = names;
            }
            catch (Exception ex)
            {
                // Non-fatal: payload will contain empty JournalName, and posting will decide requiredness.
                _log.LogWarning(ex, "Failed to fetch FSCM journal names for Company={Company}. Proceeding with empty JournalName.", company);
                result[company] = new LegalEntityJournalNames(null, null, null);
            }
        }

        return result;
    }

    // =====================================================================
    // Payload enrichment + per-WO summary logging (Currency/Worker/Warehouse/Site/LineNum)
    // =====================================================================

    private static JsonDocument EmptyValueDocument()
    {
        return JsonDocument.Parse("{\"value\":[]}");
    }

    private static string BuildHeaderOnlyWoListPayload(
        IReadOnlyList<Guid> workOrderIds,
        IReadOnlyDictionary<Guid, string?> woIdToNumber,
        IReadOnlyDictionary<Guid, string?> woIdToCompanyName,
        IReadOnlyDictionary<Guid, string?> woIdToSubProjectId,
        string correlationId,
        string runId)
    {
        var woList = new System.Text.Json.Nodes.JsonArray();

        foreach (var woId in workOrderIds)
        {
            if (!woIdToSubProjectId.TryGetValue(woId, out var subProjectId) || string.IsNullOrWhiteSpace(subProjectId))
                continue;

            woIdToCompanyName.TryGetValue(woId, out var company);
            woIdToNumber.TryGetValue(woId, out var woNumber);

            woList.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["WorkOrderGUID"] = "{" + woId.ToString("D").ToUpperInvariant() + "}",
                ["WorkOrderID"] = woNumber ?? string.Empty,
                ["Company"] = company ?? string.Empty,
                ["SubProjectId"] = subProjectId
            });
        }

        var root = new System.Text.Json.Nodes.JsonObject
        {
            ["_request"] = new System.Text.Json.Nodes.JsonObject
            {
                ["System"] = "FieldService",
                ["RunId"] = runId,
                ["CorrelationId"] = correlationId,
                ["WOList"] = woList
            }
        };

        return root.ToJsonString(new JsonSerializerOptions { PropertyNamingPolicy = null, WriteIndented = false });
    }
}
