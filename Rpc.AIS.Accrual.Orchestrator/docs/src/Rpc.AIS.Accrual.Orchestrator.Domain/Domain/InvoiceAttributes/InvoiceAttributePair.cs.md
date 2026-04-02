# InvoiceAttributePair Data Model Documentation

## Overview

The **InvoiceAttributePair** represents a simple name/value contract for invoice attributes exchanged with FSCM custom endpoints. It encapsulates the attribute’s schema name as recognized by FSCM and its corresponding value, which may be null. This record is central to:

- Fetching current attribute snapshots from FSCM
- Building deltas for updates
- Serializing update payloads

It lives in the domain layer and serves as a shared data model across services, clients, and runners.

## Data Model 📊

| Property | Type | Description |
| --- | --- | --- |
| **AttributeName** | `string` | FSCM attribute schema name (key). |
| **AttributeValue** | `string?` | The value for this attribute; `null` if absent/cleared. |


## Code Definition

```csharp
namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.InvoiceAttributes;

/// <summary>
/// Name/value pair contract used by FSCM custom endpoints.
/// AttributeName must be the FSCM attribute name.
/// </summary>
public sealed record InvoiceAttributePair(string AttributeName, string? AttributeValue);
```

## Relationships & Usage

- **Domain Namespace**

Defined under `Rpc.AIS.Accrual.Orchestrator.Core.Domain.InvoiceAttributes`, alongside `InvoiceAttributeDefinition`.

- **Runtime Mapping & Delta**

`InvoiceAttributeDeltaBuilder.BuildDelta` produces a list of `InvoiceAttributePair` to represent FS-to-FSCM updates .

- **FSCM Client Integration**

`IFscmInvoiceAttributesClient.GetCurrentValuesAsync` returns `IReadOnlyList<InvoiceAttributePair>` when snapshotting existing values .

`FscmInvoiceAttributesHttpClient` parses JSON responses into `InvoiceAttributePair` instances during `GetCurrentValuesAsync` .

- **Update Payloads**

During updates, each `InvoiceAttributePair` is serialized into the `InvoiceAttributes` array in the FSCM update envelope.

## Example Usage 🛠️

```csharp
// Creating an attribute pair for update
var wellNameAttr = new InvoiceAttributePair(
    AttributeName: "rpc_WellName",
    AttributeValue: "Alpha-1"
);

// Passing to FSCM client for update
var updates = new List<InvoiceAttributePair> { wellNameAttr };
await fscmClient.UpdateAsync(ctx, company, subProjectId, workOrderGuid,
                             workOrderId, null, null, null, 
                             fsaTaxType, fsaWellAge, fsaWorkType,
                             updates, ct);
```

## See Also

- **InvoiceAttributeDefinition**: Defines metadata and activity status of FSCM attributes.
- **IFscmInvoiceAttributesClient**: Abstraction for fetching definitions, snapshots, and updates .
- **InvoiceAttributeDeltaBuilder**: Compares FS values against FSCM snapshot to generate update pairs.