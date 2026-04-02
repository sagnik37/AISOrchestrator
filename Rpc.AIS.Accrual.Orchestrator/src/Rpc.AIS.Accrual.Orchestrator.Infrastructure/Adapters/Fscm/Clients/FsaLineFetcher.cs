// File: Rpc.AIS.Accrual.Orchestrator/src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Clients/FsaLineFetcher.cs
//
// PROD READY – FULL FILE (copy/paste)
//
// UPDATED FIX (per  clarification):
// - DO NOT try to $expand msdyn_operationalsite ( org errors 0x80060888).
// - Instead:
//   1) From msdyn_workorderproducts we already have _msdyn_warehouse_value (GUID).
//   2) Do a SECOND, deduped fetch against msdyn_warehouses by those GUIDs,
//      selecting:
//        - msdyn_warehouseid
//        - msdyn_warehouseidentifier
//        - _msdyn_operationalsite_value (and we read its @FormattedValue for site id)
//   3) Enrich each WOP row with:
//        - msdyn_warehouseidentifier (if missing)
//        - Warehouse (alias = msdyn_warehouseidentifier)
//        - msdyn_siteid (we store the operational site formatted value here, per  requirement)
//        - Site (alias = same as msdyn_siteid)
//
// NOTES / ASSUMPTIONS:
// - SiteId comes from `_msdyn_operationalsite_value@OData.Community.Display.V1.FormattedValue`
//   on msdyn_warehouses rows.
// - We DO NOT require any navigation property expansions, so this avoids the 400s.
// - This is non-breaking: if some lookups/fields are missing, enrichment simply skips.
//
// If  warehouse primary id attribute differs from msdyn_warehouseid, update WarehouseIdAttribute below.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Provides fsa line fetcher behavior.
/// </summary>
public sealed partial class FsaLineFetcherWorkflow : IFsaLineFetcher
{
    private readonly IFsaODataQueryBuilder _qb;
    private readonly IODataPagedReader _reader;
    private readonly IFsaRowFlattener _flattener;
    private readonly IWarehouseSiteEnricher _warehouseEnricher;
    private readonly IVirtualLookupNameResolver _virtualLookupResolver;

    private readonly HttpClient _http;
    private readonly ILogger<FsaLineFetcher> _log;
    private readonly FsOptions _opt;

    private const int DefaultChunkSize = 25;
    private const int PreferMaxPageSize = 5000;
    private const int MaxHttpAttempts = 6;



    // WOP warehouse lookup (GUID column on workorderproduct)

    // Warehouse table

    // Warehouse -> operational site lookup GUID column

    // Output field name expectations

    // Keep a concrete field too ( asked earlier to keep msdyn_siteid as well)
    // We will set this to the OperationalSite formatted value (site id) per  requirement.








    public FsaLineFetcherWorkflow(
        HttpClient http,
        ILogger<FsaLineFetcher> log,
        IOptions<FsOptions> opt,
        IFsaODataQueryBuilder qb,
        IODataPagedReader reader,
        IFsaRowFlattener flattener,
        IWarehouseSiteEnricher warehouseEnricher,
        IVirtualLookupNameResolver virtualLookupResolver)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _qb = qb ?? throw new ArgumentNullException(nameof(qb));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _flattener = flattener ?? throw new ArgumentNullException(nameof(flattener));
        _warehouseEnricher = warehouseEnricher ?? throw new ArgumentNullException(nameof(warehouseEnricher));
        _virtualLookupResolver = virtualLookupResolver ?? throw new ArgumentNullException(nameof(virtualLookupResolver));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _opt = opt?.Value ?? throw new ArgumentNullException(nameof(opt));

        if (string.IsNullOrWhiteSpace(_opt.DataverseApiBaseUrl))
            throw new InvalidOperationException("FsaIngestion:DataverseApiBaseUrl is missing.");

        if (_http.BaseAddress is null)
        {
            var baseUrl = _opt.DataverseApiBaseUrl.Trim();
            if (!baseUrl.EndsWith('/')) baseUrl += "/";
            _http.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
        }

        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!_http.DefaultRequestHeaders.Contains("OData-MaxVersion"))
            _http.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        if (!_http.DefaultRequestHeaders.Contains("OData-Version"))
            _http.DefaultRequestHeaders.Add("OData-Version", "4.0");

        var preferMax = _opt.PreferMaxPageSize > 0 ? _opt.PreferMaxPageSize : PreferMaxPageSize;
        var prefer = $"odata.maxpagesize={preferMax}, odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\"";
        if (!_http.DefaultRequestHeaders.TryGetValues("Prefer", out _))
            _http.DefaultRequestHeaders.Add("Prefer", prefer);

        _log.LogInformation(
         "FsaLineFetcher initialized. BaseAddress={BaseAddress} FetchStrategy=FullFetch PageSize={PageSize} MaxPages={MaxPages} Prefer={Prefer}",
         _http.BaseAddress,
         _opt.PageSize,
         _opt.MaxPages,
         prefer);

    }
    // ---------------------------------------------------------------------
    // IFsaLineFetcher (Core.Abstractions) compatibility surface
    //
    // 
    // The underlying implementation in this class prefers Guid-based APIs
    // (more efficient and less error-prone). The interface used by Functions
    // still passes WorkOrderIds as strings. These wrappers bridge that gap.
    // ---------------------------------------------------------------------

        Task<JsonDocument> IFsaLineFetcher.GetProductsAsync(RunContext context, IReadOnlyList<Guid> productIds, CancellationToken ct)
        => GetProductsAsync(productIds, ct);

async Task<JsonDocument> IFsaLineFetcher.GetOpenWorkOrdersAsync(RunContext context, CancellationToken ct)
    {
        // WorkOrderFilter is supplied via configuration (FsaIngestion.WorkOrderFilter)
        // to keep the fetcher reusable across runs.
        var filter = _opt.WorkOrderFilter;
        if (string.IsNullOrWhiteSpace(filter))
            throw new InvalidOperationException("FsaIngestion.WorkOrderFilter is not configured (required for GetOpenWorkOrdersAsync).");

        return await GetOpenWorkOrdersAsync(filter, _opt.PageSize, _opt.MaxPages, ct).ConfigureAwait(false);
    }

    async Task<JsonDocument> IFsaLineFetcher.GetWorkOrdersAsync(RunContext context, List<string> workOrderIds, CancellationToken ct)
        => await GetWorkOrdersAsync(ParseWorkOrderGuids(workOrderIds), ct).ConfigureAwait(false);

    async Task<JsonDocument> IFsaLineFetcher.GetWorkOrderProductsAsync(RunContext context, List<string> workOrderIds, CancellationToken ct)
        => await GetWorkOrderProductsAsync(ParseWorkOrderGuids(workOrderIds), ct).ConfigureAwait(false);

    async Task<JsonDocument> IFsaLineFetcher.GetWorkOrderServicesAsync(RunContext context, List<string> workOrderIds, CancellationToken ct)
        => await GetWorkOrderServicesAsync(ParseWorkOrderGuids(workOrderIds), ct).ConfigureAwait(false);

    async Task<HashSet<string>> IFsaLineFetcher.GetWorkOrderIdsWithProductsAsync(RunContext context, List<string> workOrderIds, CancellationToken ct)
    {
        var guids = ParseWorkOrderGuids(workOrderIds);
        if (guids.Count == 0) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var doc = await GetWorkOrderProductPresenceAsync(guids, ct).ConfigureAwait(false);
        return ExtractWorkOrderGuidSet(doc);
    }

    async Task<HashSet<string>> IFsaLineFetcher.GetWorkOrderIdsWithServicesAsync(RunContext context, List<string> workOrderIds, CancellationToken ct)
    {
        var guids = ParseWorkOrderGuids(workOrderIds);
        if (guids.Count == 0) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var doc = await GetWorkOrderServicePresenceAsync(guids, ct).ConfigureAwait(false);
        return ExtractWorkOrderGuidSet(doc);
    }

    // <summary>
    // Executes parse work order guids.
    // </summary>

}

