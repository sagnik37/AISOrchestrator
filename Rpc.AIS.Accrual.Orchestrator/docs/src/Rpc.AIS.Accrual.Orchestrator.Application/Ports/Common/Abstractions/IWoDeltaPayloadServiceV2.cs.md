# Work Order Delta Payload V2 Feature Documentation

## Overview 🚀

The **Work Order Delta Payload Service V2** defines an abstraction for building delta payloads with explicit options for baseline scoping and target-mode overrides. It extends the original delta payload service by allowing consumers to specify a baseline subproject and to control whether the output should perform a normal delta or force a “cancel to zero” reversal. This feature ensures greater flexibility in scenarios such as cancellation or rollback, while preserving the existing behavior for standard runs. It fits into the orchestration layer by providing a clear contract that downstream orchestrators and use cases implement when invoking delta payload generation .

## Architecture Overview

```mermaid
flowchart TB
  subgraph CoreAbstractions [Core Abstractions]
    IWoDeltaPayloadServiceV2[IWoDeltaPayloadServiceV2]
    IWoDeltaPayloadService[IWoDeltaPayloadService]
    WoDeltaBuildOptions[WoDeltaBuildOptions]
    WoDeltaTargetMode[WoDeltaTargetMode]
    WoDeltaPayloadBuildResult[WoDeltaPayloadBuildResult]
    RunContext[RunContext]
  end

  IWoDeltaPayloadServiceV2 ..> IWoDeltaPayloadService : additive to
  IWoDeltaPayloadServiceV2 --> WoDeltaBuildOptions : accepts
  WoDeltaBuildOptions --> WoDeltaTargetMode : sets
  IWoDeltaPayloadServiceV2 --> WoDeltaPayloadBuildResult : returns
  IWoDeltaPayloadServiceV2 --> RunContext : consumes
```

## Component Structure

### Business Layer

#### **IWoDeltaPayloadServiceV2**

`src/Rpc.AIS.Accrual.Orchestrator.Application/Ports/Common/Abstractions/IWoDeltaPayloadServiceV2.cs`

- **Purpose**: Defines the V2 contract for building Work Order delta payloads with explicit options.
- **Key Method**:

| Method | Description | Returns |
| --- | --- | --- |
| `BuildDeltaPayloadAsync(RunContext context, string fsaWoPayloadJson, DateTime todayUtc, WoDeltaBuildOptions options, CancellationToken ct)` | Builds a delta payload from the given FSA WO JSON using the specified options. | `Task<WoDeltaPayloadBuildResult>` |


#### **WoDeltaBuildOptions**

`src/Rpc.AIS.Accrual.Orchestrator.Application/Ports/Common/Abstractions/IWoDeltaPayloadServiceV2.cs`

- **Purpose**: Carries parameters to scope baseline processing and select target behavior.
- **Properties**:

| Property | Type | Description |
| --- | --- | --- |
| `BaselineSubProjectId` | `string?` | Optional SubProjectId to restrict the FSCM baseline for delta calculations. |
| `TargetMode` | `WoDeltaTargetMode` | Mode controlling whether to apply normal delta semantics or force cancellation. |


#### **WoDeltaTargetMode**

`src/Rpc.AIS.Accrual.Orchestrator.Application/Ports/Common/Abstractions/IWoDeltaPayloadServiceV2.cs`

- **Purpose**: Enumerates the delta build modes available in V2.
- **Members**:

| Member | Value | Description |
| --- | --- | --- |
| `Normal` | `0` | Standard delta semantics; only changes since the last run are emitted. |
| `CancelToZero` | `1` | Treats all historical lines as inactive to reverse to zero balance. |


### Data Models

#### **WoDeltaPayloadBuildResult**

`src/Rpc.AIS.Accrual.Orchestrator.Application/Ports/Common/Abstractions/IWoDeltaPayloadService.cs`

- **Purpose**: Encapsulates the result of a delta payload build operation.
- **Properties**:

| Property | Type | Description |
| --- | --- | --- |
| `DeltaPayloadJson` | `string` | The generated delta payload as a JSON string. |
| `WorkOrdersInInput` | `int` | Number of work orders provided in the input. |
| `WorkOrdersInOutput` | `int` | Number of work orders included in the output. |
| `TotalDeltaLines` | `int` | Count of new or updated journal lines. |
| `TotalReverseLines` | `int` | Count of reversal lines generated. |
| `TotalRecreateLines` | `int` | Count of recreate lines generated. |


## Integration Points

- **Additive to** `IWoDeltaPayloadService` (original interface) — consumers may implement both for backward compatibility .
- **Consume** domain types:- `RunContext` (run metadata; see Core.Domain)
- `WoDeltaPayloadBuildResult` (build output summary)
- **Options-driven behavior**:- Baseline scoping filters historical FSCM journal lines to a specific SubProject.
- Target mode toggles between normal delta and full reversal scenarios.

## Key Classes Reference 📦

| Class | Location | Responsibility |
| --- | --- | --- |
| `IWoDeltaPayloadServiceV2` | `.../Ports/Common/Abstractions/IWoDeltaPayloadServiceV2.cs` | Abstraction for V2 delta payload builder with options. |
| `WoDeltaBuildOptions` | `.../Ports/Common/Abstractions/IWoDeltaPayloadServiceV2.cs` | Carries baseline and target-mode settings. |
| `WoDeltaTargetMode` | `.../Ports/Common/Abstractions/IWoDeltaPayloadServiceV2.cs` | Enum defining delta build modes. |
| `WoDeltaPayloadBuildResult` | `.../Ports/Common/Abstractions/IWoDeltaPayloadService.cs` | Represents delta payload build results. |
| `RunContext` | `Rpc.AIS.Accrual.Orchestrator.Core.Domain` | Encapsulates execution metadata for orchestration. |


## Dependencies

- **Namespaces**:- `System`, `System.Threading`, `System.Threading.Tasks`
- `Rpc.AIS.Accrual.Orchestrator.Core.Domain` (for `RunContext`, `WoDeltaPayloadBuildResult`)
- **Downstream Implementations**: Services under `Rpc.AIS.Accrual.Orchestrator.Core.Services` implement this interface (e.g., `WoDeltaPayloadService`).

## Testing Considerations

- **Normal Mode**: Verify that only changed lines appear in the delta and counts match expectations.
- **CancelToZero Mode**: Confirm that all historical lines are reversed and marked inactive, driving balances to zero.
- **BaselineSubProjectId**: Test with and without this option to ensure historical filtering applies correctly.
- **Invalid Inputs**: Ensure appropriate exceptions on null context or empty JSON inputs.