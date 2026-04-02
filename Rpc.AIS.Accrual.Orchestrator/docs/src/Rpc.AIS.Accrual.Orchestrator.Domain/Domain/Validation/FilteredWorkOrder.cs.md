# FilteredWorkOrder 📦

## Overview

`FilteredWorkOrder` represents a single work order selected for posting after AIS‐side validation. It wraps:

- The full JSON payload of a work order.
- The journal section key used for posting.
- Only the validated journal lines to include in a downstream payload.

This record is central to both **local** and **remote** filtering steps. It ensures the orchestrator only sends valid or retryable data onward.

## Definition

```csharp
public sealed record FilteredWorkOrder(
    JsonElement WorkOrder,
    string SectionKey,
    IReadOnlyList<JsonElement> Lines);
```

## Properties

| Property | Type | Description |
| --- | --- | --- |
| **WorkOrder** | `JsonElement` | Raw JSON object for the work order, including all header and journal section fields. |
| **SectionKey** | `string` | Name of the journal section (e.g. `"WOItemLines"`). |
| **Lines** | `IReadOnlyList<JsonElement>` | Array of journal line objects approved for posting. |


## Integration in Validation Pipeline

- **Local Validation**

The `WoLocalValidator` filters out invalid lines and constructs `FilteredWorkOrder` instances for valid or retryable work orders.

- **Remote (FSCM) Validation**

The `FscmReferenceValidator` groups valid orders by company and applies a custom FSCM endpoint. It moves work orders between the valid and retryable lists based on remote failures.

- **Payload Construction**

The `WoPayloadJsonBuilder` consumes a list of `FilteredWorkOrder` and emits a JSON payload containing only approved journal lines under their resolved `SectionKey`.

## Usage Flow

1. **Parsing**- Extract raw work orders from the incoming JSON envelope.
2. **Local Filtering**- Validate required fields and build `FilteredWorkOrder` for each passing work order.
3. **Remote Filtering (optional)**- Call FSCM custom endpoint per company group.
- Adjust lists of valid and retryable `FilteredWorkOrder`.
4. **Payload Assembly**- Serialize remaining `FilteredWorkOrder` instances into the final request JSON.

This flow ensures that only fully validated journal lines are posted to FSCM, minimizing errors and retries.

---

*End of FilteredWorkOrder documentation.*