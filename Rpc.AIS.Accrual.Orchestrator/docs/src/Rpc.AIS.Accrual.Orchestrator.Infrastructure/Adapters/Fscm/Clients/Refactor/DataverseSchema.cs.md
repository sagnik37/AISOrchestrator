# Dataverse Schema Feature Documentation

## Overview

The **DataverseSchema** class centralizes all Dataverse entity, attribute, navigation, and payload constant values for FSA ingestion clients. By defining every magic string in one place, it ensures consistent OData queries, lookup expansions, and flattened JSON output across the ingestion pipeline.

This static helper is internal to the Infrastructure layer and is consumed by query builders, response readers, and row-flatteners to avoid hard-coding logical names. Any change to a Dataverse logical schema name can be made here, propagating automatically to all clients.

## Constant Groups 📦

### 1. Navigation Property Names

Defines expand-able relationship property names used in OData queries.

| Constant | Value | Description |
| --- | --- | --- |
| **Nav_ServiceAccount** | `"msdyn_serviceaccount"` | Expand work order’s company link |
| **Nav_Currency** | `"transactioncurrencyid"` | Expand currency lookup |
| **Nav_Worker** | `"msdyn_worker"` | Expand worker lookup |


### 2. Lookup Attribute Names

Logical names for lookup GUID fields.

| Constant | Value | Description |
| --- | --- | --- |
| **Lookup_SubProject** | `"_rpc_subproject_value"` | Sub-project GUID |
| **Lookup_LineProperty** | `"rpc_lineproperties"` | Line property option-set value |
| **Lookup_Department** | `"_rpc_departments_value"` | Department lookup GUID |
| **Lookup_ProductLine** | `"_rpc_productlines_value"` | Product line lookup GUID |
| **WorkOrderProductWarehouseLookupField** | `"_msdyn_warehouse_value"` | Work order product warehouse GUID |


### 3. Entity Set & Identifier Fields

Constants for querying specific entity sets by ID.

| Constant | Value | Description |
| --- | --- | --- |
| **WarehouseEntitySet** | `"msdyn_warehouses"` | OData entity set for warehouses |
| **WarehouseIdAttribute** | `"msdyn_warehouseid"` | Primary key for warehouse |
| **OperationalSiteEntitySet** | `"msdyn_operationalsites"` | OData entity set for operational sites |
| **OperationalSiteIdAttribute** | `"msdynOperationalsiteid"` | Primary key for operational site |


### 4. Flat Output Fields

Field names injected into flattened JSON payloads.

| Constant | Value | Description |
| --- | --- | --- |
| **FlatSubProjectField** | `"SubProjectId"` | Raw sub-project GUID string |
| **FlatSubProjectNameField** | `"SubProjectName"` | Display name of sub-project |
| **CurrencyCodeField** | `"isocurrencycode"` | ISO currency code lookup |
| **WorkerNumberField** | `"cdm_workernumber"` | Worker number display field |
| **FlatLinePropertyNameField** | `"LinePropertyName"` | Label of line property option-set |
| **FlatDepartmentNameField** | `"DepartmentName"` | Department display name |
| **FlatProductLineNameField** | `"ProductLineName"` | Product line display name |
| **FlatCompanyCodeField** | `"cdm_companycode"` | Company code from lookup expand |
| **FlatCompanyIdField** | `"cdm_companyid"` | Company GUID from lookup expand |
| **FlatSiteIdField** | `"msdyn_siteid"` | Site ID field from operational site |


### 5. Payload Alias Fields

Aliases for warehouse and site in final payload.

| Constant | Value | Description |
| --- | --- | --- |
| **PayloadWarehouseField** | `"Warehouse"` | High-level warehouse alias |
| **PayloadSiteField** | `"Site"` | High-level site alias |


### 6. OData Formatted Suffix

| Constant | Value | Description |
| --- | --- | --- |
| **ODataFormattedSuffix** | `"@OData.Community.Display.V1.FormattedValue"` | Suffix to access formatted lookup |


## Usage Example

```csharp
// Build a warehouse OData query selecting ID and identifier
var url = $"{DataverseSchema.WarehouseEntitySet}"
        + "?$select="
        + $"{DataverseSchema.WarehouseIdAttribute},"
        + $"{DataverseSchema.WarehouseIdentifierField}";
// url → "msdyn_warehouses?$select=msdyn_warehouseid,msdyn_warehouseidentifier"
```

## Related Components 🔗

- **FsaODataQueryBuilder** (`.../FsaODataQueryBuilder.cs`): Builds OData query URLs using these schema constants.
- **FsaRowFlattener** (`.../FsaRowFlattener.cs`): Flattens expanded lookups into top-level JSON properties.
- **WarehouseSiteEnricher** (`.../WarehouseSiteEnricher.cs`): Uses lookup fields and flat field constants to enrich rows.
- **VirtualLookupNameResolver** (`.../VirtualLookupNameResolver.cs`): Reads formatted lookup values via `ODataFormattedSuffix`.

## Key Class Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| **DataverseSchema** | `src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/Refactor/DataverseSchema.cs` | Central store for all Dataverse schema constant values |


## Dependencies

- None external; this static helper is referenced by various FSA ingestion infrastructure clients.