// File: .../Infrastructure/Clients/Refactor/FsaClientAbstractions.cs
// SRP/SOLID abstractions around query-building, paging, flattening, and enrichment.
// : These abstractions are implemented to preserve existing behavior (no contract changes for IFsaLineFetcher).

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Defines i fsa o data query builder behavior.
/// </summary>
public interface IFsaODataQueryBuilder
{
    string BuildOpenWorkOrdersRelative(string filter, int pageSize);
    string BuildWorkOrdersRelative(IReadOnlyCollection<Guid> ids, int pageSize);
    string BuildWorkOrderProductsRelative(IReadOnlyCollection<Guid> workOrderIds, int pageSize);
    string BuildWorkOrderServicesRelative(IReadOnlyCollection<Guid> workOrderIds, int pageSize);
    string BuildProductsRelative(IReadOnlyCollection<Guid> productIds, int pageSize);

    string BuildWorkOrderProductPresenceRelative(IReadOnlyCollection<Guid> workOrderIds);
    string BuildWorkOrderServicePresenceRelative(IReadOnlyCollection<Guid> workOrderIds);

    string BuildWarehousesByIdsRelative(IReadOnlyCollection<Guid> warehouseIds);
    string BuildOperationalSitesByIdsRelative(IReadOnlyCollection<Guid> operationalSiteIds);
    string BuildVirtualLookupByIdsRelative(string entitySetName, string idAttribute, IReadOnlyCollection<Guid> ids);
}

/// <summary>
/// Defines io data paged reader behavior.
/// </summary>
public interface IODataPagedReader
{
    Task<JsonDocument> ReadAllPagesAsync(
        HttpClient http,
        string initialRelativeUrl,
        int maxPages,
        string logEntityName,
        CancellationToken ct);
}

/// <summary>
/// Defines i fsa row flattener behavior.
/// </summary>
public interface IFsaRowFlattener
{
    JsonDocument FlattenWorkOrderCompanyFromExpand(JsonDocument workOrdersDoc);
}

/// <summary>
/// Defines i warehouse site enricher behavior.
/// </summary>
public interface IWarehouseSiteEnricher
{
    Task<JsonDocument> EnrichWopLinesFromWarehousesAsync(HttpClient http, JsonDocument baseLines, CancellationToken ct);
}

/// <summary>
/// Defines i virtual lookup name resolver behavior.
/// </summary>
public interface IVirtualLookupNameResolver
{
    Task<JsonDocument> EnrichLinesWithVirtualLookupNamesAsync(HttpClient http, JsonDocument baseLines, CancellationToken ct);
}
