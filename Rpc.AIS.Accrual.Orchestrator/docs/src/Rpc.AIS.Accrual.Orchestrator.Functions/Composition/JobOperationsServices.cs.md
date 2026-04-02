# Job Operations Services Feature Documentation

## Overview ⚙️

The **Job Operations Services** file defines key abstractions and placeholder implementations for FSCM-related lifecycle steps and the customer-change workflow within the Functions layer’s durable activities. By depending on interfaces rather than concrete clients, durable orchestrations remain decoupled from external FSCM endpoint details and can execute in development environments without configured systems.

This design supports the “job operations” orchestrators by providing:

- A project lifecycle interface for updating project stages and synchronizing attributes.
- A customer-change orchestrator interface for executing full end-to-end customer-change processes.
- A no-op implementation that logs intended calls until real FSCM integrations are available.

## Architecture Overview

```mermaid
flowchart TB
    subgraph DurableActivities
        Activities[JobOperationsActivities]
    end
    subgraph CompositionServices
        IFscm[IFscmProjectLifecycle]
        ICustomerChange[ICustomerChangeOrchestrator]
        NoopFscm[NoopFscmProjectLifecycle]
        CustomerChangeOrch[CustomerChangeOrchestrator]
    end
    Activities -->|injects| IFscm
    Activities -->|injects| ICustomerChange
    IFscm <|.. NoopFscm
    ICustomerChange <|.. CustomerChangeOrch
```

## Component Structure

### Business Layer

#### Interfaces

| Interface | Purpose |
| --- | --- |
| **IFscmProjectLifecycle** | Defines FSCM project lifecycle steps (stage update and attribute sync) for durable activities. |
| **ICustomerChangeOrchestrator** | Encapsulates the full customer-change business process, returning the new sub-project ID. |


**IFscmProjectLifecycle**

```csharp
Task SetProjectStageAsync(RunContext ctx, Guid workOrderGuid, string stage, CancellationToken ct);
Task SyncJobAttributesToProjectAsync(RunContext ctx, Guid workOrderGuid, CancellationToken ct);
```

- **SetProjectStageAsync**: Update FSCM project stage.
- **SyncJobAttributesToProjectAsync**: Synchronize job attributes into FSCM.

**ICustomerChangeOrchestrator**

```csharp
Task<CustomerChangeResultDto> ExecuteAsync(
    RunContext ctx,
    Guid workOrderGuid,
    string rawRequestJson,
    CancellationToken ct);
```

- **ExecuteAsync**: Executes customer change flow and returns a `CustomerChangeResultDto`.

#### Data Models

| Record | Properties | Description |
| --- | --- | --- |
| **CustomerChangeResultDto** | *NewSubProjectId* 🔑 | The identifier of the newly created sub-project. |


#### Implementations

| Class | Implements | Behavior |
| --- | --- | --- |
| **NoopFscmProjectLifecycle** | IFscmProjectLifecycle | Logs intended FSCM calls as warnings; does not invoke external systems. |


```csharp
public Task SetProjectStageAsync(RunContext ctx, Guid workOrderGuid, string stage, CancellationToken ct)
{
    _log.LogWarning(
        "NOOP: SetProjectStageAsync skipped (endpoints not configured). " +
        "WorkOrderGuid={WorkOrderGuid} Stage={Stage} RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem}",
        workOrderGuid, stage, ctx.RunId, ctx.CorrelationId, ctx.SourceSystem);
    return Task.CompletedTask;
}
```

```csharp
public Task SyncJobAttributesToProjectAsync(RunContext ctx, Guid workOrderGuid, CancellationToken ct)
{
    _log.LogWarning(
        "NOOP: SyncJobAttributesToProjectAsync skipped (endpoints not configured). " +
        "WorkOrderGuid={WorkOrderGuid} RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem}",
        workOrderGuid, ctx.RunId, ctx.CorrelationId, ctx.SourceSystem);
    return Task.CompletedTask;
}
```

## Dependency Injection Registration

These services are registered in DI, enabling the Functions layer to resolve the placeholders by default until FSCM endpoints are finalized.

```csharp
services.AddSingleton<IFscmProjectLifecycle, NoopFscmProjectLifecycle>();
services.AddSingleton<ICustomerChangeOrchestrator, CustomerChangeOrchestrator>();
```

## Dependencies

- **Microsoft.Extensions.Logging** (`ILogger<T>`) for structured logging.
- **Rpc.AIS.Accrual.Orchestrator.Core.Domain.RunContext** to carry run identifiers and context data through service calls.

## Testing Considerations

- **NoopFscmProjectLifecycle** can be used in unit tests to verify that orchestration logic invokes lifecycle steps without requiring real FSCM integrations.
- **CustomerChangeResultDto** enables straightforward assertions on the outcome of the customer-change workflow.