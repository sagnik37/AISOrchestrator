# FSCM Accounting Period HTTP Resolver Feature Documentation

## Overview

The **FscmAccountingPeriodResolver.Http** component handles all HTTP interactions for resolving fiscal calendars and periods from the FSCM OData service. It encapsulates base-URL resolution, request construction, logging, response reading, and error classification.

By centralizing HTTP concerns, it enables the broader **FscmAccountingPeriodResolver** to focus on query semantics, parsing, caching, and business logic.

## Architecture Overview

```mermaid
flowchart TB
    subgraph Clients
        HttpClient[FscmAccountingPeriodHttpClient]
    end

    subgraph DataAccessLayer
        ResolverHttp[FscmAccountingPeriodResolver Http]
        ResolverCore[FscmAccountingPeriodResolver (Other partials)]
    end

    subgraph ExternalServices
        ODataAPI[FSCM OData API]
    end

    HttpClient --> ResolverHttp
    ResolverHttp --> ResolverCore
    ResolverCore --> ODataAPI
```

## Component Structure

### Data Access Layer

#### **FscmAccountingPeriodResolver.Http** (`src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/FscmAccountingPeriodResolver.Http.cs`)

- **Purpose:**

⚙️ Encapsulates HTTP request logic (base-URL resolution, headers, logging, error handling) for OData calls used in period resolution.

- **Dependencies:**- `HttpClient _http`
- `FscmOptions _endpoints`
- `ILogger<FscmAccountingPeriodResolver> _logger`

#### Methods

| Method | Signature | Description | Returns |
| --- | --- | --- | --- |
| ResolveBaseUrlOrThrow | `private string ResolveBaseUrlOrThrow()` | Retrieves the configured base URL from `FscmOptions.BaseUrl`. Throws `InvalidOperationException` if missing or empty. | `string` |
| SendODataAsync  | `private async Task<string> SendODataAsync(RunContext context, string step, string url, CancellationToken ct)` | - Builds `HttpRequestMessage` (GET, `Accept: application/json`, `x-run-id`/`x-correlation-id`) |


- Logs **START** with step name, URL, run & correlation IDs
- Measures elapsed time
- Reads full response body
- Logs **END** with status code, elapsed ms, and byte count
- Classifies errors:- 401/403 → `UnauthorizedAccessException`
- 429 or ≥500 → `HttpRequestException`
- ≥400 → `InvalidOperationException`
- Returns raw JSON body string.  | `Task<string>` |

## Error Handling

- **Authentication Errors** (401/403):

Throws `UnauthorizedAccessException` including HTTP status and truncated body.

- **Transient Failures** (429 or ≥500):

Throws `HttpRequestException` to allow higher-level retry logic.

- **Client Errors** (400–499 except auth):

Throws `InvalidOperationException`, preventing further unsafe processing.

- **Empty or Oversized Body**:

The body is always read; trimming is applied when embedding in exception messages via `Trim(body)`.

## Caching Strategy

This HTTP helper does **not** implement caching. All caching of calendar IDs, period statuses, and “out-of-window” dates occurs in other partial class files.

## Dependencies

- System.Net.Http
- Microsoft.Extensions.Logging
- Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options (for `FscmOptions`)
- Rpc.AIS.Accrual.Orchestrator.Core.Abstractions (for `RunContext`)

## Key Classes Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| FscmAccountingPeriodResolver.Http | `.../Adapters/Fscm/Clients/FscmAccountingPeriodResolver.Http.cs` | HTTP façade for OData calls (base URL, headers, logging, error handling) |
| FscmAccountingPeriodHttpClient | `.../Clients/FscmAccountingPeriodHttpClient.cs` | Thin facade implementing `IFscmAccountingPeriodClient` via resolver |


## Testing Considerations

- **Mocking ****`HttpClient`** to simulate various HTTP statuses (401, 429, 500, 400).
- **Verifying Logging** of START/END events with correct step names and metrics.
- **Exception Paths**: ensure each status classification throws the correct exception type.
- **Header Propagation**: test that `x-run-id` and `x-correlation-id` are set when present in `RunContext`.