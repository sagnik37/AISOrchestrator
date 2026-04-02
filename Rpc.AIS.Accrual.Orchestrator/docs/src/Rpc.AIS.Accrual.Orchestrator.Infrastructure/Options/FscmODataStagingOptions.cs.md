# FSCM OData Staging Feature Documentation

## Overview

The **FSCM OData Staging** feature introduces an alternative posting pipeline for journal entries into the Finance and Supply Chain Management (FSCM) system. Instead of the traditional single-endpoint approach, it separates the payload into:

- A header entity POST
- Line items submitted via an OData changeset

This separation enables more granular control, deterministic voucher naming, and throttled concurrency per legal entity. The feature remains disabled by default to preserve existing deployments.

## Architecture Overview

```mermaid
flowchart TB
    config[Configuration\n(FscmODataStagingOptions)]
    orchestrator[Journal Orchestrator\n(FscmJournalPoster)]
    stagingApi[FSCM OData Staging API]
    defaultApi[FSCM Legacy Posting API]

    config -->|Enabled? true| orchestrator
    orchestrator --> stagingApi
    config -->|Enabled? false| orchestrator
    orchestrator --> defaultApi

    subgraph "FSCM Integration"
        stagingApi
        defaultApi
    end
```

## Component Structure

### Configuration

#### **FscmODataStagingOptions** (`src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Options/FscmODataStagingOptions.cs`)

Holds all settings controlling the OData staging workflow.

- **SectionName** (const string)

Configuration section key: `"Fscm:ODataStaging"`

- **Enabled** (bool, default `false`)

Master toggle to activate OData staging.

- **BaseUrl** (string?, default `null`)

FSCM host URL override (e.g. `https://<env>.operations.dynamics.com`).

Falls back to `Endpoints:FscmBaseUrl` when unset.

- **MaxConcurrentJournalsPerLegalEntity** (int, default `2`)

AIS-side throttle for simultaneous staging operations per `dataAreaId` to reduce HTTP 429 responses.

- **VoucherPrefix** (string, default `"AIS"`)

Prefix for deterministic voucher numbers on project items.

- **HeaderIdempotencyPrefix** (string, default `"AIS"`)

Prefix stored in journal header description for idempotency.

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| SectionName | string | — | Configuration section key |
| Enabled | bool | false | Master feature toggle |
| BaseUrl | string? | null | FSCM host URL override; falls back if empty |
| MaxConcurrentJournalsPerLegalEntity | int | 2 | Concurrent journal staging throttle per legal entity |
| VoucherPrefix | string | "AIS" | Deterministic voucher prefix |
| HeaderIdempotencyPrefix | string | "AIS" | Idempotency key prefix stored in header description |


## Integration Points

- **Startup Registration**

Bound in `Functions/Program.cs` via:

```csharp
  services.AddOptions<FscmODataStagingOptions>()
          .Bind(cfg.GetSection(FscmODataStagingOptions.SectionName))
          .ValidateOnStart();
```

- **Orchestrator Consumption**

The `FscmJournalPoster` (or equivalent posting pipeline) reads these options to:

- Decide between legacy posting and OData staging
- Throttle concurrent staging requests
- Construct base URLs and idempotency keys

## Important Notes

```card
{
    "title": "Feature Toggle",
    "content": "Set Fscm:ODataStaging:Enabled to true to activate the OData staging workflow."
}
```

```card
{
    "title": "URL Fallback",
    "content": "If BaseUrl is unset, the system uses the legacy Endpoints:FscmBaseUrl value."
}
```

## Key Classes Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| FscmODataStagingOptions | `.../Infrastructure/Options/FscmODataStagingOptions.cs` | Configuration for FSCM OData staging feature |


## Testing Considerations

- **Default State**

Verify `Enabled == false` results in legacy posting path.

- **URL Resolution**

Test scenarios with and without `BaseUrl` to ensure proper fallback.

- **Concurrency Throttling**

Simulate more than `MaxConcurrentJournalsPerLegalEntity` simultaneous operations and confirm throttling behavior.

- **Idempotency Key Generation**

Confirm `HeaderIdempotencyPrefix` appears correctly in journal header descriptions when staging is enabled.