# Released Distinct Products Client Feature Documentation

## Overview

The **Released Distinct Products Client** provides a single point of access to retrieve **ProjCategoryId** and **RpcProjCategoryId** for a list of ERP item numbers from the FSCM **CDSReleasedDistinctProducts** OData endpoint.

By relying exclusively on this interface, the orchestrator guarantees consistency in category mapping during accrual payload enrichment, avoiding discrepancies that might arise from Dataverse lookups.

## Interface: IFscmReleasedDistinctProductsClient 🎯

Defined in

`src/Rpc.AIS.Accrual.Orchestrator.Application/Ports/Common/Abstractions/IFscmReleasedDistinctProductsClient.cs`

```csharp
namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions
{
    /// <summary>
    /// Fetches project category identifiers from FSCM CDSReleasedDistinctProducts 
    /// for a set of ItemNumbers.
    /// This is the ONLY source of ProjCategoryId/RPCProjCategoryId.
    /// </summary>
    public interface IFscmReleasedDistinctProductsClient
    {
        Task<IReadOnlyDictionary<string, ReleasedDistinctProductCategory>>
            GetCategoriesByItemNumberAsync(
                RunContext ctx,
                IReadOnlyList<string> itemNumbers,
                CancellationToken ct);
    }

    /// <summary>
    /// FSCM category ids for a released distinct product.
    /// </summary>
    public sealed record ReleasedDistinctProductCategory(
        string ItemNumber,
        string? ProjCategoryId,
        string? RpcProjCategoryId
    );
}
```

### Method Summary

| Method | Parameters | Returns |
| --- | --- | --- |
| **GetCategoriesByItemNumberAsync** | • **ctx**: `RunContext` (request metadata, logging) <br> • **itemNumbers**: list of ERP item numbers <br> • **ct**: `CancellationToken` | `Task<IReadOnlyDictionary<string, ReleasedDistinctProductCategory>>` |


## Domain Model: ReleasedDistinctProductCategory

| Property | Type | Description |
| --- | --- | --- |
| **ItemNumber** | `string` | The ERP item number. |
| **ProjCategoryId** | `string?` | FSCM project category identifier. |
| **RpcProjCategoryId** | `string?` | FSCM RPC project category identifier. |


## Core Implementations

- **FscmReleasedDistinctProductsHttpClient**

Performs chunked OData GET requests against `CDSReleasedDistinctProducts`, handles retries and JSON parsing.

`src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/FscmReleasedDistinctProductsHttpClient.cs`

- **CachedFscmReleasedDistinctProductsClient**

Wraps an inner `IFscmReleasedDistinctProductsClient`, reads/writes a memory cache, prevents stampedes, and applies positive & negative TTL policies.

`src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/CachedFscmReleasedDistinctProductsClient.cs`

## Dependency Injection Registration

In the Functions project’s `Program.cs`, the clients are wired up as follows:

```csharp
services.AddHttpClient<FscmReleasedDistinctProductsHttpClient>();

services.AddSingleton<IFscmReleasedDistinctProductsClient>(sp =>
{
    var inner = sp.GetRequiredService<FscmReleasedDistinctProductsHttpClient>();
    return new CachedFscmReleasedDistinctProductsClient(
        inner,
        sp.GetRequiredService<IMemoryCache>(),
        sp.GetRequiredService<IOptions<FscmReleasedDistinctProductsCacheOptions>>(),
        sp.GetRequiredService<ILogger<CachedFscmReleasedDistinctProductsClient>>());
});
```

## Usage Example

Within the **FsaDeltaPayloadUseCase**, this client enriches work order lines with category IDs:

```csharp
public partial class FsaDeltaPayloadUseCase : IFsaDeltaPayloadUseCase
{
    private readonly IFscmReleasedDistinctProductsClient _releasedDistinctProducts;
    // ...

    public FsaDeltaPayloadUseCase(
        IFscmReleasedDistinctProductsClient releasedDistinctProducts,
        /* other deps */)
    {
        _releasedDistinctProducts = releasedDistinctProducts;
    }

    public async Task<GetFsaDeltaPayloadResultDto> BuildFullFetchAsync(/*...*/, CancellationToken ct)
    {
        // ... after fetching IDs ...
        var categoryMap = await _releasedDistinctProducts
            .GetCategoriesByItemNumberAsync(runContext, itemNumbers, ct);
        // Use categoryMap to enrich each payload line...
    }
}
```

## Dependencies & Integration

- **RunContext**: Carries run identifiers & timestamps for logging/tracing.
- **ReleasedDistinctProductCategory**: Value object returned by the interface.
- **FscmOptions**, **IResilientHttpExecutor**: Config and resilience support in the HTTP client.
- **IMemoryCache**, **FscmReleasedDistinctProductsCacheOptions**: Caching support in the decorator.
- **FsaDeltaPayloadUseCase**: Consumes this interface to enrich payloads with category info.

---

This abstraction cleanly separates the **“what”** (fetch category IDs) from the **“how”** (OData HTTP calls, caching), enabling flexible implementations and consistent category mapping across the application.