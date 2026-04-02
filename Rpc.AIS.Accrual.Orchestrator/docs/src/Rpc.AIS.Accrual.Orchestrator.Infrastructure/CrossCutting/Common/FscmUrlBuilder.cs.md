# FSCM URL Builder Feature Documentation

## Overview

The **FSCM URL Builder** centralizes construction of REST endpoint URLs for FSCM integrations. It encapsulates two core concerns: resolving a unified or legacy base URL and concatenating paths in a consistent, slash-safe manner. By extracting URL semantics into a single utility, individual HTTP client classes remain focused on orchestration logic (SRP), reducing duplication and configuration errors.

This utility ensures:

- **Preferred** use of a single, unified FSCM host (`FscmOptions.BaseUrl`).
- **Fallback** to per-endpoint legacy overrides when the unified host is not configured.
- **Consistent** trimming of trailing slashes and insertion of leading slashes for paths.

## Architecture Overview

The FSCM URL Builder resides in the **Infrastructure** layer under **Cross-Cutting Utilities**. It is invoked by various FSCM HTTP client implementations to determine the full request URL before dispatching HTTP messages.

Infrastructure Layer

  ↳ CrossCutting/Common

    • FscmUrlBuilder

## Component Structure

### Utilities Layer

#### **FscmUrlBuilder** (`src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/CrossCutting/Common/FscmUrlBuilder.cs`)

- **Purpose**: Provide a central, reusable mechanism for resolving base URLs and composing full FSCM endpoint URLs.
- **Responsibilities**:- Determine which base URL to use (`ResolveFscmBaseUrl`).
- Concatenate base URL and endpoint path safely (`BuildUrl`).

```csharp
using System;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Utilities
{
    /// <summary>
    /// Centralized FSCM URL construction and base-url resolution.
    /// Extracted to keep HTTP clients focused on orchestration (SRP) and make URL semantics reusable.
    /// </summary>
    public static class FscmUrlBuilder
    {
        /// <summary>
        /// Preferred: FscmOptions.FscmBaseUrl (single unified host).
        /// Fallback: legacy per-endpoint base URLs.
        /// </summary>
        public static string ResolveFscmBaseUrl(FscmOptions endpoints, string? legacyBaseUrl, string legacyName)
        {
            if (endpoints is null) throw new ArgumentNullException(nameof(endpoints));
            if (!string.IsNullOrWhiteSpace(endpoints.BaseUrl))
                return endpoints.BaseUrl.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(legacyBaseUrl))
                return legacyBaseUrl.TrimEnd('/');
            throw new InvalidOperationException(
                $"FSCM base URL is not configured. Set 'Fscm:BaseUrl' (preferred) or legacy override under 'Endpoints:{legacyName}'.");
        }

        /// <summary>
        /// Executes build url.
        /// </summary>
        public static string BuildUrl(string baseUrl, string path)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException("FSCM base URL is not configured.");
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("FSCM path is not configured.");
            var baseFixed = baseUrl.TrimEnd('/');
            var pathFixed = path.StartsWith('/') ? path : "/" + path;
            return baseFixed + pathFixed;
        }
    }
}
```

### Utility Methods

| Method | Signature | Description | Exceptions |
| --- | --- | --- | --- |
| ResolveFscmBaseUrl | `string ResolveFscmBaseUrl(FscmOptions endpoints, string? legacyBaseUrl, string legacyName)` | Returns `BaseUrl` from `endpoints` if set; else uses `legacyBaseUrl`; errors if neither is configured. | `ArgumentNullException` if `endpoints` is null; `InvalidOperationException` if no URL configured. |
| BuildUrl | `string BuildUrl(string baseUrl, string path)` | Trims trailing slash from `baseUrl`, ensures `path` begins with `'/'`, and concatenates them. | `InvalidOperationException` if `baseUrl` or `path` is null/whitespace. |


## Error Handling

- **ArgumentNullException**

Thrown by `ResolveFscmBaseUrl` when the `endpoints` parameter is `null`.

- **InvalidOperationException**- In `ResolveFscmBaseUrl` when neither the unified nor legacy base URL is set.
- In `BuildUrl` when `baseUrl` or `path` is missing or empty.

These exceptions enforce early detection of misconfiguration.

## Dependencies

- **FscmOptions** (`Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options`)

Provides configured values for:

- `BaseUrl` (unified FSCM host)
- Legacy endpoint overrides (e.g., `SingleWorkOrderBaseUrlOverride`, `SubProjectBaseUrlOverride`, etc.)

## Key Classes Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| FscmUrlBuilder | `Infrastructure/CrossCutting/Common/FscmUrlBuilder.cs` | Centralizes and reuses URL resolution and construction. |


## Testing Considerations

Key scenarios to verify in unit tests:

- **Unified BaseUrl configured**: `ResolveFscmBaseUrl` returns trimmed `BaseUrl`.
- **Fallback to legacy**: `ResolveFscmBaseUrl` returns trimmed `legacyBaseUrl` when `BaseUrl` is blank.
- **Missing configuration**: `ResolveFscmBaseUrl` throws `InvalidOperationException`.
- **Path concatenation**:- `BuildUrl("https://host/", "path")` → `https://host/path`
- `BuildUrl("https://host", "/path")` → `https://host/path`
- Missing `path` or `baseUrl` triggers appropriate exceptions.

Testing should cover trimming behavior and exception messages for clarity of misconfiguration.