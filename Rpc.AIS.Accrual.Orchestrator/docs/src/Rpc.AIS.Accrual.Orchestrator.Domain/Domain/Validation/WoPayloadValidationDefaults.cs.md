# WoPayloadValidationDefaults Feature Documentation

## Overview

The **WoPayloadValidationDefaults** class provides a safe, empty default result for Work Order (WO) payload validation.

It ensures that any part of the orchestration pipeline can obtain a valid `WoPayloadValidationResult` when no data or failures exist.

By centralizing this default, developers avoid repetitive boilerplate and guarantee consistency across the application.

## Architecture Overview

```mermaid
classDiagram
    direction TB
    subgraph DomainValidation [Domain - Validation]
        WoPayloadValidationDefaults
        WoPayloadValidationResult
        WoPayloadValidationFailure
    end

    WoPayloadValidationDefaults ..> WoPayloadValidationResult : creates
    WoPayloadValidationDefaults ..> WoPayloadValidationFailure : references
```

## Component Structure

### 1. Domain Layer

#### **WoPayloadValidationDefaults** (`src/Rpc.AIS.Accrual.Orchestrator.Domain/Domain/Validation/WoPayloadValidationDefaults.cs`)

- **Purpose:**

Centralizes a default, empty validation result for WO payloads.

- **Responsibilities:**- Supply a `WoPayloadValidationResult` with zero work orders and no failures.
- Prevent null or unexpected payloads in downstream processing.

##### Key Methods

| Method | Description | Returns |
| --- | --- | --- |
| EmptyResult | Returns a default `WoPayloadValidationResult` with no work orders and no failures. | `WoPayloadValidationResult` |


```csharp
public static class WoPayloadValidationDefaults
{
    public static WoPayloadValidationResult EmptyResult() 
        => new(
            "{}",                                 // filteredPayloadJson
            Array.Empty<WoPayloadValidationFailure>(), // failures
            0,                                    // workOrdersBefore
            0,                                    // workOrdersAfter
            "{}",                                 // retryablePayloadJson
            Array.Empty<WoPayloadValidationFailure>(), // retryableFailures
            0                                     // retryableWorkOrdersAfter
        );
}
```

## Key Classes Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| WoPayloadValidationDefaults | src/Rpc.AIS.Accrual.Orchestrator.Domain/Domain/Validation/WoPayloadValidationDefaults.cs | Provides a single entry point for an empty validation result. |
| WoPayloadValidationResult | src/Rpc.AIS.Accrual.Orchestrator.Domain/Domain/Validation/WoPayloadValidationResult.cs | Represents the outcome of WO payload validation. |
| WoPayloadValidationFailure | src/Rpc.AIS.Accrual.Orchestrator.Domain/Domain/Validation/WoPayloadValidationFailure.cs | Models an individual validation failure for a WO line. |


## Dependencies

- **Namespaces:**- `System`
- `Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation`

- **Types Used:**- `WoPayloadValidationResult`
- `WoPayloadValidationFailure`

These dependencies ensure the default result aligns with the domain’s validation result model.

## Integration Points

- **Usage Scenario:**

Whenever a validation pipeline or orchestrator step needs to represent “no work orders” or “no failures,” it should call `WoPayloadValidationDefaults.EmptyResult()`.

- **Downstream Consumption:**

Other services and pipelines consume the returned `WoPayloadValidationResult` without requiring null checks or custom initialization.

## Error Handling

This class encapsulates an error-free, empty result. There is no exception path in `EmptyResult()`, guaranteeing a non-null, valid object in all contexts.

## Testing Considerations

- **Unit Test Scenario:**

Verify that `EmptyResult()` returns a `WoPayloadValidationResult` where:

- `FilteredPayloadJson == "{}"`
- `Failures` is an empty list
- `WorkOrdersBefore == 0`
- `WorkOrdersAfter == 0`
- `RetryablePayloadJson == "{}"`
- `RetryableFailures` is an empty list
- `RetryableWorkOrdersAfter == 0`