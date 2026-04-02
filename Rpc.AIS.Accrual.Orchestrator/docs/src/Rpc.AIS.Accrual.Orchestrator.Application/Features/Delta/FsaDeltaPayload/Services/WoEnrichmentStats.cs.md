# WoEnrichmentStats Class Documentation

## Overview

The `WoEnrichmentStats` class serves as a simple data holder for tracking per-work-order enrichment metrics during the outbound FSA delta payload processing. As JSON payloads are enriched with additional fields (e.g., operations dates, currency, resource IDs), an instance of this class accumulates counters and identifiers. After enrichment, these stats drive informational logging to aid observability and troubleshooting.

## Namespace

- **Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload**

## Properties

| Name | Type | Description |
| --- | --- | --- |
| WorkorderId | string? | The business-facing identifier (ID) of the work order |
| WorkorderGuidRaw | string? | The raw GUID string for the work order |
| Company | string? | The company name associated with this work order |
| EnrichedLinesTotal | int | Total number of journal lines that were enriched |
| EnrichedHourLines | int | Count of enriched hour-type journal lines |
| EnrichedExpLines | int | Count of enriched expense-type journal lines |
| EnrichedItemLines | int | Count of enriched item-type journal lines |
| FilledCurrency | int | Number of currency fields that were injected/fixed |
| FilledResourceId | int | Number of resource ID fields that were injected |
| FilledWarehouse | int | Number of warehouse fields that were injected |
| FilledSite | int | Number of site fields that were injected |
| FilledLineNum | int | Number of line-number fields that were injected |
| FilledOperationsDate | int | Number of operations-date fields that were filled (private setter; use `MarkFilledOperationsDate`) |


All setter-backed counters start at zero.

## Method

```csharp
public void MarkFilledOperationsDate()
```

- Increments the `FilledOperationsDate` counter by one.
- Called when an operations date is injected into a journal line during JSON enrichment.

## Typical Usage

1. **Initialization**

A `List<WoEnrichmentStats>` is created before payload traversal.

1. **Population**

As each work order and its lines are processed by `FsaDeltaPayloadJsonInjector.CopyRootWithInjectionAndStats`, a new `WoEnrichmentStats` is instantiated, populated with identifiers, and counters are updated whenever enrichment occurs.

1. **Logging**

After the JSON write completes, the `FsExtrasInjector` iterates over the stats list and logs a summary line per work order capturing all counters and identifiers.

### Example in `FsExtrasInjector`

```csharp
var stats = new List<WoEnrichmentStats>();
FsaDeltaPayloadJsonInjector.CopyRootWithInjectionAndStats(
    input.RootElement,
    w,
    extrasDict,
    stats);
...
foreach (var s in stats)
{
    _log.LogInformation(
        "WO Enrichment Summary " +
        "WorkorderGUID={WorkorderGuid} WorkorderID={WorkorderId} Company={Company} " +
        "EnrichedLinesTotal={Total} Hour={Hour} Expense={Expense} Item={Item} " +
        "Currency={Currency} ResourceId={ResourceId} Warehouse={Warehouse} Site={Site} LineNum={LineNum} OperationsDate={OperationsDate}",
        s.WorkorderGuidRaw,
        s.WorkorderId,
        s.Company,
        s.EnrichedLinesTotal,
        s.EnrichedHourLines,
        s.EnrichedExpLines,
        s.EnrichedItemLines,
        s.FilledCurrency,
        s.FilledResourceId,
        s.FilledWarehouse,
        s.FilledSite,
        s.FilledLineNum,
        s.FilledOperationsDate);
}
```

This produces structured log entries that help monitor how many and which fields were enriched per work order.