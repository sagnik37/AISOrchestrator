# WO Payload Validation Result Feature Documentation

## Overview

The **WoPayloadValidationResult** class encapsulates the outcome of AIS-side validation for Work Order (WO) payloads. It provides:

- A filtered payload JSON containing only valid work orders.
- Detailed lists of **non-retryable** and **retryable** failures.
- Counts of work orders before and after filtering for both normal and retryable groups.

This result drives downstream logic in the orchestration pipeline, enabling:

- Deterministic decision-making on which work orders to post.
- Fail-fast behavior on critical errors.
- Retry handling for transient or recoverable validation issues.

## Component Structure

### 📦 Data Models

#### **WoPayloadValidationResult**

*Location:* `src/Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation/WoPayloadValidationResult.cs`

Represents the outcome of WO payload validation. It stores filtered payloads, failure details, and work-order counts for both immediate and retryable groups.

**Constructor Overloads:**

- `WoPayloadValidationResult(string filteredPayloadJson, IReadOnlyList<WoPayloadValidationFailure> failures, int workOrdersBefore, int workOrdersAfter)`

Initializes retryable payload to `"{}"` with no failures.

- `WoPayloadValidationResult(string filteredPayloadJson, IReadOnlyList<WoPayloadValidationFailure> failures, int workOrdersBefore, int workOrdersAfter, string retryablePayloadJson, IReadOnlyList<WoPayloadValidationFailure> retryableFailures, int retryableWorkOrdersAfter)`

Full control over both normal and retryable payloads and failures.

**Core Properties:**

| Property | Type | Description |
| --- | --- | --- |
| **FilteredPayloadJson** | `string` | JSON string of work orders deemed valid after validation. |
| **Failures** | `IReadOnlyList<WoPayloadValidationFailure>` | Non-retryable validation failures (blocking errors). |
| **WorkOrdersBefore** | `int` | Count of work orders in the original payload. |
| **WorkOrdersAfter** | `int` | Count of work orders retained in `FilteredPayloadJson`. |
| **RetryablePayloadJson** | `string` | JSON string of work orders flagged for retry. |
| **RetryableFailures** | `IReadOnlyList<WoPayloadValidationFailure>` | Validation failures marked as retryable (transient issues). |
| **RetryableWorkOrdersAfter** | `int` | Count of work orders in `RetryablePayloadJson`. |
| **HasFailures** | `bool` | `true` if at least one non-retryable failure exists. |
| **HasRetryables** | `bool` | `true` if there are retryable work orders or retryable failures. |
| **HasFailFast** | `bool` | `true` if any failure (normal or retryable) has `ValidationDisposition.FailFast`. |


```csharp
// Example: full constructor with null-guard logic
public WoPayloadValidationResult(
    string filteredPayloadJson,
    IReadOnlyList<WoPayloadValidationFailure> failures,
    int workOrdersBefore,
    int workOrdersAfter,
    string retryablePayloadJson,
    IReadOnlyList<WoPayloadValidationFailure> retryableFailures,
    int retryableWorkOrdersAfter)
{
    FilteredPayloadJson = filteredPayloadJson ?? throw new ArgumentNullException(nameof(filteredPayloadJson));
    Failures = failures ?? Array.Empty<WoPayloadValidationFailure>();
    WorkOrdersBefore = workOrdersBefore;
    WorkOrdersAfter = workOrdersAfter;
    RetryablePayloadJson = retryablePayloadJson ?? "{}";
    RetryableFailures = retryableFailures ?? Array.Empty<WoPayloadValidationFailure>();
    RetryableWorkOrdersAfter = retryableWorkOrdersAfter;
}
```

## Key Classes Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| **WoPayloadValidationResult** | `src/Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation/WoPayloadValidationResult.cs` | Encapsulates results of WO payload validation, including filtering and failure details. |
| **WoPayloadValidationFailure** | Same namespace (`Core.Domain.Validation`) | Describes individual validation failure (code, message, disposition). |
| **ValidationDisposition** | Enum in `Core.Domain.Validation` | Defines failure dispositions: `Invalid`, `Retryable`, `FailFast`, etc. |


## Error Handling 🛑

- **Null-Guarding:**- Throws `ArgumentNullException` if `filteredPayloadJson` is `null`.
- Replaces any `null` failures or retry lists with empty defaults.

- **Defaults:**- `RetryablePayloadJson` defaults to `"{}"`.
- `Failures` and `RetryableFailures` default to empty arrays.

## Dependencies 🔗

- **Framework:**- `System`
- `System.Collections.Generic`
- `System.Linq`
- **Domain Types:**- `WoPayloadValidationFailure`
- `ValidationDisposition`

## Testing Considerations 🧪

- Validate that the **default constructor** sets retryable payload to `"{}"` and empty failure lists.
- Confirm **null arguments** in `filteredPayloadJson` trigger `ArgumentNullException`.
- Verify **HasFailures**, **HasRetryables**, and **HasFailFast** accurately reflect provided failure lists and dispositions.
- Test boundary cases: zero work orders, only retryable failures, mixed dispositions.