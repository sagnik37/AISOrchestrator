# Status Change Service Feature Documentation

## Overview

The **Status Change Service** handles incoming status update events for domain entities. It encapsulates the receipt and logging of status changes—such as transitions from “Pending” to “Completed”—and carries rich context for diagnostics and auditing. Business systems emit status change requests, and this service ensures they are recorded reliably via the AIS logging framework.

This feature lives in the **Core Application** layer as an abstraction. Implementers—such as the default `StatusChangeService`—inject an `IAisLogger` to forward status change details to external monitoring or auditing sinks. By decoupling the interface from its implementation, other adapters can be introduced without affecting high-level orchestration logic.

## Architecture Overview

```mermaid
flowchart TB
  subgraph Functions & Orchestrators
    A[Durable Orchestrator<br/>or HTTP Trigger] -->|injects| B[Business Use Cases]
  end

  subgraph Application Layer
    B -->|calls| C[IStatusChangeService]
  end

  subgraph Core.Abstractions
    C[IStatusChangeService<br/>HandleAsync(request, ct)]
  end

  subgraph Core.Services
    D[StatusChangeService]<br/>IAisLogger
    C -->|implemented by| D
  end
```

## Component Structure

### Business Layer

#### **IStatusChangeService** (`src/Rpc.AIS.Accrual.Orchestrator.Application/Ports/Common/Abstractions/IStatusChangeService.cs`)

- **Purpose:** Defines a contract for handling status change requests in a uniform way.
- **Method Summary:**

| Method | Description | Returns |
| --- | --- | --- |
| HandleAsync | Process a `StatusChangeRequest` payload | `Task` |


### Core.Services (Deprecated)

#### **StatusChangeService** (`src/Rpc.AIS.Accrual.Orchestrator.Core.Services/StatusChangeService.cs`)

> ```csharp public interface IStatusChangeService { Task HandleAsync(StatusChangeRequest request, CancellationToken ct); } ```

- Implements `IStatusChangeService` by delegating to an `IAisLogger`.
- **Behavior:**- Validates non-null request.
- Determines a run identifier (fallback `"RUN-NA"` if absent).
- Emits an info-level log entry `"Status change received."` with all properties.
- No external side-effects beyond logging.

### Data Models

#### **StatusChangeRequest**

> ```csharp public sealed class StatusChangeService : IStatusChangeService { private readonly IAisLogger _ais;
> public StatusChangeService(IAisLogger ais) { _ais = ais ?? throw new ArgumentNullException(nameof(ais)); }
> public Task HandleAsync(StatusChangeRequest request, CancellationToken ct) { if (request is null) throw new ArgumentNullException(nameof(request));
> var runId = string.IsNullOrWhiteSpace(request.RunId) ? "RUN-NA" : request.RunId!; var step = "StatusChange"; var data = new { request.EntityName, request.RecordId, request.OldStatus, request.NewStatus, request.CorrelationId, request.Message, request.Payload }; return _ais.InfoAsync(runId, step, "Status change received.", data, ct); } } ```

Carries all information about a status transition event.

| Property | Type | Description |
| --- | --- | --- |
| EntityName | `string` | Name of the entity whose status changed |
| RecordId | `string` | Identifier of the record |
| OldStatus | `string` | Previous status value |
| NewStatus | `string` | Updated status value |
| Message | `string?` | Optional descriptive message |
| RunId | `string?` | Optional correlation run identifier |
| CorrelationId | `string?` | Optional correlation trace identifier |
| Payload | `object?` | Arbitrary payload for context |


## Error Handling

> ```csharp public sealed record StatusChangeRequest( string EntityName, string RecordId, string OldStatus, string NewStatus, string? Message, string? RunId, string? CorrelationId, object? Payload); ```

- **Null Requests:** `HandleAsync` throws `ArgumentNullException` if the `request` is null.
- **RunId Fallback:** An empty or whitespace `RunId` becomes `"RUN-NA"` to ensure logging always has a run context.

## Dependencies

- **IAisLogger:** Core logging abstraction for AIS telemetry.
- **StatusChangeRequest:** Domain model carrying event details.
- **CancellationToken:** To observe cancellation.

## Integration Points

- **Orchestrations & Functions:** Injected into higher-level controllers or durable orchestrators to record status change steps.
- **Monitoring:** Downstream `IAisLogger` implementations can ship logs to external systems (Application Insights, Splunk, etc.).

## Key Classes Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| IStatusChangeService | `Application/Ports/Common/Abstractions/IStatusChangeService.cs` | Defines the status change handling contract |
| StatusChangeService | `Core/Services/StatusChangeService.cs` | Default implementation using AIS logger |
| StatusChangeRequest | `Domain/Domain/StatusChangeRequest.cs` | Encapsulates status change event data |


## Testing Considerations

- **HandleAsync Null Check:** Verify `ArgumentNullException` on null `request`.
- **Logging Payload:** Ensure `IAisLogger.InfoAsync` is called with expected `runId`, `step`, and data object.
- **RunId Resolution:** Test scenarios with and without `RunId` in the request.