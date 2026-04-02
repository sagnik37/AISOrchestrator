# FSA Service Line Mapper Feature Documentation

## Overview

🔄 The **FSA Service Line Mapper** defines a contract for transforming raw Dataverse JSON rows into rich domain objects. It decouples JSON parsing from business logic, ensuring consistent mapping across the accrual orchestrator.

This mapper resides in the Core Abstractions layer and underpins the **snapshot builder**, which assembles delta payloads for downstream processing. By isolating mapping responsibilities, it improves maintainability and testability of the payload assembly process.

## Architecture Overview

```mermaid
flowchart TB
    subgraph CoreAbstractions [Core Abstractions Layer]
        ISLMapper[IFsaServiceLineMapper]
    end
    subgraph Mappers [Mapping Services]
        FSLMapper[FsaServiceLineMapper] -->|implements| ISLMapper
    end
    subgraph SnapshotBuilder [Snapshot Builder]
        SnapshotBuilder[FsaSnapshotBuilder] -->|uses| ISLMapper
    end
```

## Component Structure

### Core Abstractions Layer

#### **IFsaServiceLineMapper** (`src/Rpc.AIS.Accrual.Orchestrator.Application/Ports/Common/Abstractions/IFsaServiceLineMapper.cs`)

- Defines how to map a Dataverse work order service row into a domain object.

| Method | Description | Returns |
| --- | --- | --- |
| `Map(JsonElement row, Guid workOrderId, string workOrderNumber)` | Transforms a JSON element representing a service line into an `FsaServiceLine`. | `FsaServiceLine` |


## Domain Model

#### **FsaServiceLine** (`src/Rpc.AIS.Accrual.Orchestrator.Core.Domain/FsaDeltaDtos.cs`)

Represents a single service line item in the final delta payload.

| Property | Type | Description |
| --- | --- | --- |
| `LineId` | `Guid` | Unique identifier of the service line. |
| `WorkOrderId` | `Guid` | Identifier of the parent work order. |
| `WorkOrderNumber` | `string` | Human-readable work order number. |
| `ProductId` | `Guid?` | Identifier of the associated service product. |
| `Duration` | `decimal?` | Service duration (hours). |
| `UnitCost` | `decimal?` | Cost per unit of service. |
| `FsaUnitPrice` | `decimal?` | Unit price as provided by Dataverse. |
| `UnitAmount` | `decimal?` | Mapped to `FsaUnitPrice` for service lines. |
| `Currency` | `string?` | ISO currency code. |
| `Unit` | `string?` | Unit of measure. |
| `JournalDescription` | `string?` | Description from the work order service record. |
| `DiscountAmount` | `decimal?` | Line-level discount amount. |
| `DiscountPercent` | `decimal?` | Line-level discount percentage. |
| `SurchargeAmount` | `decimal?` | Line-level surcharge amount. |
| `SurchargePercent` | `decimal?` | Line-level surcharge percentage. |
| `CustomerProductReference` | `string?` | Customer-provided reference. |
| `CalculatedUnitPrice` | `decimal?` | Computed unit price (delta enrichment). |
| `LineProperty` | `string?` | Line property metadata. |
| `Department` | `string?` | Department metadata. |
| `ProductLine` | `string?` | Product line metadata. |
| `IsActive` | `bool?` | Indicates if the line is active. |
| `DataAreaId` | `string?` | Dataverse data area identifier. |
| `Printable` | `bool?` | Printable flag. |
| `TaxabilityType` | `string?` | Taxability setting for the line. |
| `OperationsDateUtc` | `DateTime?` | Operation timestamp in UTC. |


## Dependencies

- **System.Text.Json** for JSON parsing.
- **Rpc.AIS.Accrual.Orchestrator.Core.Domain** for the `FsaServiceLine` model.
- **System** for base types.

## Integration Points

- **Implementation**: `FsaServiceLineMapper` in the Core.Services.FsaDeltaPayload.Mappers namespace implements this interface.
- **Snapshot Builder**: `FsaSnapshotBuilder` invokes `Map` to assemble service lines within `FsaDeltaSnapshot`.

## Key Classes Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| `IFsaServiceLineMapper` | `Application/Ports/Common/Abstractions/IFsaServiceLineMapper.cs` | Contract for JSON-to-domain mapping of service rows. |
| `FsaServiceLine` | `Core.Domain/FsaDeltaDtos.cs` | Domain object for service line data. |
| `FsaServiceLineMapper` | `Core.Services.FsaDeltaPayload.Mappers/FsaServiceLineMapper.cs` | Concrete mapper using JSON helper utilities. |
| `FsaSnapshotBuilder` | `Core.Services.FsaDeltaPayload.Mappers/FsaSnapshotBuilder.cs` | Builds delta snapshots from mapped service lines. |


## Testing Considerations

- Verify `Map` returns a fully populated `FsaServiceLine` for a well-formed JSON element.
- Ensure `Map` throws `InvalidOperationException` when the required `msdyn_workorderserviceid` property is missing.
- Test mapping of optional fields such as `TaxabilityType` and `CustomerProductReference` when those properties are present or null.