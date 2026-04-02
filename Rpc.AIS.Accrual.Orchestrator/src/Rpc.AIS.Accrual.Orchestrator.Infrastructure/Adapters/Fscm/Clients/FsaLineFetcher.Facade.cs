// File: Rpc.AIS.Accrual.Orchestrator/src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Clients/FsaLineFetcher.cs
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
/// Thin facade delegating all IFsaLineFetcher behavior to <see cref="FsaLineFetcherWorkflow"/>.
/// This preserves existing DI wiring and behavior while enforcing SRP at the class level.
/// </summary>
public sealed class FsaLineFetcher : IFsaLineFetcher
{
    private readonly IFsaLineFetcher _inner;

    public FsaLineFetcher(

        HttpClient http,
        ILogger<FsaLineFetcher> log,
        IOptions<FsOptions> opt,
        IFsaODataQueryBuilder qb,
        IODataPagedReader reader,
        IFsaRowFlattener flattener,
        IWarehouseSiteEnricher warehouseEnricher,
        IVirtualLookupNameResolver virtualLookupResolver
    )
    {
        _inner = new FsaLineFetcherWorkflow(http, log, opt, qb, reader, flattener, warehouseEnricher, virtualLookupResolver);
    }

    public Task<JsonDocument> GetOpenWorkOrdersAsync(RunContext context, CancellationToken ct)
        => _inner.GetOpenWorkOrdersAsync(context, ct);

    public Task<JsonDocument> GetWorkOrdersAsync(RunContext context, List<string> workOrderIds, CancellationToken ct)
        => _inner.GetWorkOrdersAsync(context, workOrderIds, ct);

    public Task<JsonDocument> GetWorkOrderProductsAsync(RunContext context, List<string> workOrderIds, CancellationToken ct)
        => _inner.GetWorkOrderProductsAsync(context, workOrderIds, ct);

    public Task<JsonDocument> GetWorkOrderServicesAsync(RunContext context, List<string> workOrderIds, CancellationToken ct)
        => _inner.GetWorkOrderServicesAsync(context, workOrderIds, ct);

    public Task<HashSet<string>> GetWorkOrderIdsWithProductsAsync(RunContext context, List<string> workOrderIds, CancellationToken ct)
        => _inner.GetWorkOrderIdsWithProductsAsync(context, workOrderIds, ct);

    public Task<HashSet<string>> GetWorkOrderIdsWithServicesAsync(RunContext context, List<string> workOrderIds, CancellationToken ct)
        => _inner.GetWorkOrderIdsWithServicesAsync(context, workOrderIds, ct);

    public Task<JsonDocument> GetProductsAsync(RunContext context, IReadOnlyList<Guid> productIds, CancellationToken ct)
        => _inner.GetProductsAsync(context, productIds, ct);
}
