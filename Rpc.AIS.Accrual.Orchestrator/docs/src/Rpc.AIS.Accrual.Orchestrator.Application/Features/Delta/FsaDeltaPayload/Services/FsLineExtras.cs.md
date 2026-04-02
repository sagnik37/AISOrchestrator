# 🔧 FsLineExtras Record Documentation

## Overview

The **FsLineExtras** record encapsulates additional metadata for Field Service lines in the FSA delta payload. It carries optional enrichment data—such as currency, worker identifier, warehouse/site details, line number, and operations date—that originates from downstream FS systems. This metadata drives conditional injection of missing properties into outbound JSON payloads.

## Purpose

- Centralize all possible line‐level extras into a single immutable object.
- Enable payload enrichers to determine if there’s any data to inject.
- Support logging of which fields were enriched per work order line.

## Properties

| Property | Type | Description |
| --- | --- | --- |
| **Currency** | `string?` | ISO currency code (e.g., "USD") |
| **WorkerNumber** | `string?` | Identifier for the resource or technician |
| **WarehouseIdentifier** | `string?` | Unique code of the warehouse supplying the item |
| **SiteId** | `string?` | Identifier of the operational site |
| **LineNum** | `int?` | Order index of the line within the work order |
| **OperationsDate** | `string?` | Original operations date as received from Field Service (NEW) |


## Method

### `HasAny() : bool`

Determines whether the record contains **any** non‐empty enrichment data.

- Returns **true** if at least one property is non-null, non-empty, or has a value.
- Returns **false** when all fields are null, empty, or unset.

```csharp
public bool HasAny() =>
    !string.IsNullOrWhiteSpace(Currency)
 || !string.IsNullOrWhiteSpace(WorkerNumber)
 || !string.IsNullOrWhiteSpace(WarehouseIdentifier)
 || !string.IsNullOrWhiteSpace(SiteId)
 || LineNum.HasValue
 || !string.IsNullOrWhiteSpace(OperationsDate);
```

## Usage Context

- **Construction**

Instances are created during the lookup‐map build phase (see `FsaDeltaPayloadLookupMaps.BuildLineExtrasMapForFinalPayload`), where each JSON row may emit one `FsLineExtras` per line GUID.

- **Enrichment**

The `FsExtrasInjector` consumes a dictionary of `FsLineExtras` keyed by line GUID. It injects missing JSON properties into each journal line when `HasAny()` is true and logs enrichment statistics.

- **Pipeline Integration**

In the FSA enrichment pipeline, the **FsExtrasEnrichmentStep** invokes the injector only if any extras exist, preserving performance when no enrichment is needed.

## Example

```csharp
// Sample creation of line extras for injection
var extras = new FsLineExtras(
    Currency: "EUR",
    WorkerNumber: "TECH-007",
    WarehouseIdentifier: "WH-A1",
    SiteId: "SITE-42",
    LineNum: 3,
    OperationsDate: "2023-03-15"
);

// Conditional enrichment
if (extras.HasAny())
{
    // Proceed to inject into payload and log stats
}
```

---

<details>

<summary>📦 Relationships</summary>

- **FsaDeltaPayloadLookupMaps**

Builds a `Dictionary<Guid, FsLineExtras>` by reading FS JSON documents.

- **FsExtrasInjector**

Applies the extras to outbound JSON via `InjectFsExtrasAndLogPerWoSummary`, leveraging `HasAny()` to skip empty records.

- **FsExtrasEnrichmentStep**

Integrates into the enrichment pipeline; it checks for the presence of extras and delegates to the injector.

</details>