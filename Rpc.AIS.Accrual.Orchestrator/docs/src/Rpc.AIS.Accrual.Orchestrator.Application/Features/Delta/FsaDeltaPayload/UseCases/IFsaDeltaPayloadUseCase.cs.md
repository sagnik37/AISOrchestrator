# FSA Delta Payload Use Case Feature Documentation

## Overview

The **FSA Delta Payload** feature orchestrates the construction of JSON payloads representing Field Service work orders for downstream accrual processing. It provides two primary behaviors:

- **Full Fetch**: Builds a payload for all open work orders matching a filter.
- **Single Work Order**: Builds a payload for one work order regardless of its status (e.g., closed or cancelled).

This interface decouples orchestration logic from Azure Functions, enabling clean separation between presentation (Durable Functions) and application (Use Case) layers. It drives data retrieval, enrichment and JSON assembly for both real-time job operations and batch ingestion.

## Architecture Overview

```mermaid
flowchart LR
  subgraph Functions Layer
    A[FsaDeltaPayloadOrchestrator]:::component
  end
  subgraph Use Case Layer
    B[IFsaDeltaPayloadUseCase]:::interface
    C[FsaDeltaPayloadUseCase]:::component
  end
  subgraph Domain & Services
    D[IFsaLineFetcher]
    E[DeltaComparer]
    F[IFsaSnapshotBuilder]
    G[IFsaDeltaPayloadEnricher]
    H[IFsaDeltaPayloadEnrichmentPipeline]
    I[IFscmBaselineFetcher]
    J[IFscmReleasedDistinctProductsClient]
    K[IFscmLegalEntityIntegrationParametersClient]
    L[IEmailSender]
  end

  A -->|invokes| B
  B <|..| C
  C --> D & E & F & G & H & I & J & K & L

  classDef component fill:#E8F1FA,stroke:#0366D6;
  classDef interface fill:#F1F8E9,stroke:#2E7D32;
```

## Component Structure

### Business Layer

#### IFsaDeltaPayloadUseCase

**Path**: `src/Rpc.AIS.Accrual.Orchestrator.Core.UseCases.FsaDeltaPayload/IFsaDeltaPayloadUseCase.cs`

Defines orchestration entry points for delta payload builds .

| Method | Description | Returns |
| --- | --- | --- |
| BuildFullFetchAsync(input, opt, ct) | Builds a JSON payload for all open work orders matching `opt.WorkOrderFilter`. | `Task<GetFsaDeltaPayloadResultDto>` |
| BuildSingleWorkOrderAnyStatusAsync(input, opt, ct) | Builds a JSON payload for one work order GUID, even if it’s closed or cancelled. Intended for job-operation flows like Cancel. | `Task<GetFsaDeltaPayloadResultDto>` |


### Domain Models

#### GetFsaDeltaPayloadInputDto

Carries run metadata and optional work order GUID for single fetch.

| Property | Type | Description |
| --- | --- | --- |
| RunId | string | Unique identifier for this run |
| CorrelationId | string | Distributed tracing correlation ID |
| TriggeredBy | string | Source trigger (e.g., "Timer", "Http") |
| WorkOrderGuid | string? | Optional GUID for single fetch |
| DurableInstanceId | string? | Durable Function instance ID |


#### GetFsaDeltaPayloadResultDto

Encapsulates outbound payload JSON and metadata.

| Property | Type | Description |
| --- | --- | --- |
| PayloadJson | string | JSON array of work order snapshots |
| ProductDeltaLinkAfter | string? | Cursor for next product delta page (unused in core) |
| ServiceDeltaLinkAfter | string? | Cursor for next service delta page (unused in core) |
| WorkOrderNumbers | IReadOnlyList<string> | List of included work order numbers |


#### FsaDeltaPayloadRunOptions

Minimal runtime options mapped from `FsOptions`.

| Property | Type | Description |
| --- | --- | --- |
| WorkOrderFilter | string? | Filter expression for open work orders |


## Integration Points

- **Azure Functions**: `FsaDeltaPayloadOrchestrator` (Functions Layer) invokes the use case .
- **Data Fetching**: Relies on `IFsaLineFetcher` to retrieve products, services and headers from Dataverse.
- **Enrichment Pipeline**: Applies OCP-friendly enrichment steps via `IFsaDeltaPayloadEnrichmentPipeline`.
- **Notifications**: Uses `IEmailSender` to notify on missing SubProject scenarios.

## Key Classes Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| IFsaDeltaPayloadUseCase | Core.UseCases.FsaDeltaPayload/IFsaDeltaPayloadUseCase.cs | Defines delta payload build contracts |
| FsaDeltaPayloadUseCase | Core.UseCases.FsaDeltaPayload/FsaDeltaPayloadUseCase.cs | Implements orchestration for full and single-WO payload builds |
| GetFsaDeltaPayloadInputDto | Core.Domain/FsaDeltaActivityDtos.cs | Encapsulates input parameters for payload builds |
| GetFsaDeltaPayloadResultDto | Core.Domain/FsaDeltaActivityDtos.cs | Holds outbound JSON and metadata |
| FsaDeltaPayloadRunOptions | Core.Options/FsaDeltaPayloadRunOptions.cs | Carries runtime options from Functions boundary |
| FsaDeltaPayloadOrchestrator | Functions/Services/FsaDeltaPayloadOrchestrator.cs | Thin Durable Function adapter |


## Dependencies

- **System.Threading**, **System.Threading.Tasks**
- **Rpc.AIS.Accrual.Orchestrator.Core.Domain** (DTOs, snapshots)
- **Rpc.AIS.Accrual.Orchestrator.Core.Options** (Run options)
- Various service abstractions (`IFsaLineFetcher`, `IFsaSnapshotBuilder`, etc.)

## Testing Considerations

- **Null Input**: Methods throw `ArgumentNullException` if `input` is null.
- **Invalid GUID**: Single-WO method returns empty payload for invalid or empty GUID.
- **No Matching Work Orders**: Full fetch with no open WOs returns an empty payload without error.
- **Enrichment Steps**: Validate each enrichment step transforms payload correctly.

---

*This documentation captures the purpose, structure, and usage of the FSA Delta Payload Use Case interface and its surrounding models and orchestrator.*