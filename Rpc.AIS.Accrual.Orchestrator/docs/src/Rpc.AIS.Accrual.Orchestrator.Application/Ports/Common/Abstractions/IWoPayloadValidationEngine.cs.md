# Wo Payload Validation Engine Feature Documentation

## Overview

The **Work Order (WO) Payload Validation Engine** provides a deterministic, side-effect-free mechanism to validate AIS Work Order payload JSON before posting to FSCM. It ensures that only valid records advance in the orchestration pipeline, reducing downstream errors and improving system reliability. The engine returns both detailed failure information and a filtered payload containing only valid work orders .

## Architecture Overview

```mermaid
flowchart TB
    subgraph Core.Abstractions
        IEngine[IWoPayloadValidationEngine]
        Result[WoPayloadValidationResult]
        Failure[WoPayloadValidationFailure]
    end
    subgraph Validation.Pipeline
        EngineImpl[WoPayloadValidationEngine<br/>Core.Services]
        LocalVal[IWoLocalValidator]
        Rules[Rule Pipeline<br/>IWoPayloadRule[]]
        RemoteClient[IFscmWoPayloadValidationClient]
    end
    IEngine --> EngineImpl
    EngineImpl --> Rules
    Rules --> LocalVal
    Rules --> RemoteClient
    EngineImpl --> Result
    Result --> Failure
```

This diagram shows the interface contract in **Core.Abstractions**, its implementation in **Core.Services**, and dependencies on local and remote validation components.

## Component Structure

### Business Layer

#### **IWoPayloadValidationEngine**

(`src/Rpc.AIS.Accrual.Orchestrator.Application/Ports/Common/Abstractions/IWoPayloadValidationEngine.cs`)

- **Purpose:** Defines the contract for AIS WO payload validation and filtering prior to FSCM posting.
- **Responsibilities:**- Validate the input JSON payload.
- Identify invalid or retryable records.
- Produce a filtered payload JSON containing only valid work orders.
- **Key Method:**

| Method | Signature | Returns |
| --- | --- | --- |
| ValidateAndFilterAsync | `Task<WoPayloadValidationResult> ValidateAndFilterAsync(RunContext context, JournalType journalType, string woPayloadJson, CancellationToken ct)` | `WoPayloadValidationResult` |


  Implements: a single asynchronous call that inspects the payload, applies local rules and optional FSCM validations, and returns a comprehensive result .

## Data Models

### WoPayloadValidationResult

(`src/Rpc.AIS.Accrual.Orchestrator.Domain/Domain/Validation/WoPayloadValidationResult.cs`)

| Property | Type | Description |
| --- | --- | --- |
| FilteredPayloadJson | `string` | JSON string containing only valid work orders after validation. |
| Failures | `IReadOnlyList<WoPayloadValidationFailure>` | List of all detected validation failures. |
| WorkOrdersBefore | `int` | Number of work orders before filtering. |
| WorkOrdersAfter | `int` | Number of work orders remaining after filtering. |
| RetryablePayloadJson | `string` | JSON string containing work orders marked retryable. |
| RetryableFailures | `IReadOnlyList<WoPayloadValidationFailure>` | List of failures with a retryable disposition. |
| RetryableWorkOrdersAfter | `int` | Count of retryable work orders after validation. |
| HasFailures | `bool` | `true` if any failures exist. |
| HasRetryables | `bool` | `true` if any retryable failures or orders exist. |
| HasFailFast | `bool` | `true` if any failure has `FailFast` disposition. |


### WoPayloadValidationFailure

(`src/Rpc.AIS.Accrual.Orchestrator.Domain/Domain/Validation/WoPayloadValidationFailure.cs`)

| Property | Type | Description |
| --- | --- | --- |
| WorkOrderGuid | `Guid` | Unique identifier of the work order. |
| WorkOrderNumber | `string?` | Optional human-readable identifier for the work order. |
| JournalType | `JournalType` | The type of journal being processed. |
| WorkOrderLineGuid | `Guid?` | Identifier of the specific line within the work order, if applicable. |
| Code | `string` | Machine-readable validation error code. |
| Message | `string` | Human-readable description of the failure. |
| Disposition | `ValidationDisposition` | Indicates handling strategy (`Valid`, `Invalid`, `Retryable`, `FailFast`). |


## Integration Points

- **RunContext**: Carries correlation data (run ID, correlation ID) throughout validation.
- **JournalType**: Enumerates supported journal profiles.
- **IWoLocalValidator**: Applies AIS-side business rules before remote checks.
- **IFscmWoPayloadValidationClient**: Optionally invokes an FSCM endpoint for custom validation.

## Key Classes Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| IWoPayloadValidationEngine | `.../Abstractions/IWoPayloadValidationEngine.cs` | Contract for validating and filtering WO payload JSON. |
| WoPayloadValidationResult | `.../Domain/Validation/WoPayloadValidationResult.cs` | Encapsulates the outcome of payload validation. |
| WoPayloadValidationFailure | `.../Domain/Validation/WoPayloadValidationFailure.cs` | Represents an individual validation failure detail. |


## Testing Considerations

- Validate empty or whitespace payloads produce an empty filtered JSON (`"{}"`) with zero counts.
- Supply payloads with known invalid work orders to ensure `Failures` captures correct codes and messages.
- Confirm retryable dispositions appear in `RetryablePayloadJson` and `RetryableFailures`.
- Simulate remote validation failures (e.g., transport errors) to verify fail-closed behavior.