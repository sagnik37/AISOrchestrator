# FSA Delta Payload Metadata & Lookup Utilities

## Overview

This partial class extends the core `FsaDeltaPayloadUseCase` with helper methods that extract metadata and build lookup maps from JSON documents. These utilities support:

- Mapping work order headers to their identifying names.
- Collecting GUID lookup IDs from line-item JSON arrays.
- Building product enrichment maps for type and item number.

They serve as reusable, **private** static methods within the use-case orchestration, isolating JSON parsing logic and ensuring consistent lookup behavior.

## Component Structure

### Business Layer: `FsaDeltaPayloadUseCase` (Metadata & Lookups)

File: `src/Rpc.AIS.Accrual.Orchestrator.Application/Features/Delta/FsaDeltaPayload/UseCases/FsaDeltaPayloadUseCase.MetadataAndLookups.cs`

This partial class defines three core methods:

| Method Signature | Parameters | Description | Returns |
| --- | --- | --- | --- |
| `BuildWorkOrderNumberMap(JsonDocument woHeaders)` | `woHeaders` – JSON doc of work order header records | Reads each record’s `msdyn_workorderid` and `msdyn_name`, mapping IDs to names. | `Dictionary<Guid, string>` |
| `CollectLookupIds(JsonDocument doc, HashSet<Guid> target, params string[] keys)` | `doc` – JSON doc; `target` – pre-initialized GUID set; <br>`keys` – lookup property names | Iterates rows under `value`, parses the first valid GUID from any of the specified keys, adds to `target`. | `void` |
| `BuildProductEnrichmentMaps(JsonDocument products)` | `products` – JSON doc of product records | For each `productid`, reads `msdyn_fieldserviceproducttype` to determine “Inventory” or “Non-Inventory”, and maps `msdyn_productnumber`. | `(Dictionary<Guid, string> productTypeById, Dictionary<Guid, string?> itemNumberById)` |


#### BuildWorkOrderNumberMap

- **Purpose:** Extracts work order numbers from header JSON.
- **Behavior:**1. Validates `woHeaders` is non-null and contains a `value` array.
2. For each row:- Parses `msdyn_workorderid` → `Guid id`.
- Reads `msdyn_name` → string `num`.
- If `num` is non-empty, adds `map[id] = num`.
- **Example:**

```cs
  var map = BuildWorkOrderNumberMap(woHeadersJson);
  // map: { Guid("...") : "WO-12345", ... }
```

#### CollectLookupIds

- **Purpose:** Gathers lookup GUIDs from arbitrary JSON arrays.
- **Validations:**- Throws if `doc` or `target` is null.
- Throws if `keys` is null or empty.
- **Behavior:**1. Confirms `value` array exists.
2. For each JSON `row`, iterates `keys`:- If property exists and is a string parsable as `Guid`, adds it to `target` and breaks inner loop.
- **Usage:**

```cs
  var productIds = new HashSet<Guid>();
  CollectLookupIds(woProductsJson, productIds, "_msdyn_product_value", "_productid_value");
```

#### BuildProductEnrichmentMaps

- **Purpose:** Builds two lookup maps for downstream product enrichment.
- **Behavior:**1. Validates `products` document structure.
2. For each row:- Parses `productid` → `Guid pid`.
- Reads optional numeric `msdyn_fieldserviceproducttype`:- `690970000` → `"Inventory"`
- `690970001` → `"Non-Inventory"`
- otherwise → `"Unknown"`
- Reads optional string `msdyn_productnumber`.
- Populates- `productTypeById[pid] = ptype`
- `itemNumberById[pid] = productNumber`
- **Example:**

```cs
  var (typeMap, itemMap) = BuildProductEnrichmentMaps(productsJson);
  // typeMap: { Guid("...") : "Inventory", ... }
  // itemMap: { Guid("...") : "ABC-123", ... }
```

## Dependencies

- **System.Text.Json** – for `JsonDocument` parsing.
- **FsaDeltaPayloadJsonUtil** (static) – provides `TryGuid(JsonElement, string, out Guid)`.
- **Core Domain Types:** `Guid`, `Dictionary<,>`, `HashSet<>`.

## Key Classes Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| FsaDeltaPayloadUseCase (partial) | src/.../UseCases/FsaDeltaPayload/FsaDeltaPayloadUseCase.MetadataAndLookups.cs | Implements metadata extraction and lookup-map building utilities. |


## Testing Considerations

- **Null Arguments:** `CollectLookupIds` must throw for null `doc`, `target`, or empty `keys`.
- **Empty JSON Arrays:** Methods should return empty maps/sets without exceptions.
- **Invalid GUID Values:** Rows with non-GUID strings should be skipped gracefully.
- **Type Mapping:** Verify numeric codes other than `690970000/690970001` map to `"Unknown"`.

## Dependencies & Integration

These lookup methods are invoked by the main `BuildFullFetchAsync` and `BuildSingleWorkOrderAnyStatusAsync` flows in `FsaDeltaPayloadUseCase`. They prepare:

- Work order number lookups for snapshot headers.
- Product and service line enrichment via GUID collections.
- Product-type and item-number maps for detailed payload building.