# AisValidationPostErrorBuilder Utility Documentation

## Overview

The **AisValidationPostErrorBuilder** is a static utility designed to transform detailed work-order validation failures into concise post-errors suited for downstream processing or reporting. By capping the number of errors and summarizing each failure into a lightweight `PostError`, this builder helps prevent overwhelming logs or UI components with verbose validation details.

This utility fits into the broader orchestration pipeline after local and remote (FSCM) validation of work-order payloads. It prepares a user-friendly error list that can be attached to post-results, email notifications, or telemetry events.

## Purpose & Benefits

- **Summarization**: Converts rich `WoPayloadValidationFailure` objects into simplified `PostError` records.
- **Throttling**: Limits the number of errors via a configurable `max` parameter (default 50).
- **Consistency**: Applies a uniform prefix (`AIS_VALIDATION_`) and formatting to all error codes and messages.
- **Integration-Ready**: Produced `PostError` instances integrate seamlessly with posting handlers and error aggregators in the orchestration pipeline.

## Method: BuildConcisePostErrors 🛠️

| Signature | Description |
| --- | --- |
| `public static IReadOnlyList<PostError> BuildConcisePostErrors(IReadOnlyList<WoPayloadValidationFailure> failures, int max = 50)` | Builds a list of at most **max** `PostError` objects from the provided validation failures. Returns an empty list if `failures` is null or empty. |


### Parameters

- **failures**

 Type: `IReadOnlyList<WoPayloadValidationFailure>`

 A collection of detailed validation failure instances.

- **max** *(optional)*

 Type: `int`

 Maximum number of failures to include in the result. Defaults to 50.

### Returns

- Type: `IReadOnlyList<PostError>`
- A list of `PostError` records representing each failure, up to the `max` limit.

### Behavior

1. **Empty check**: If `failures` is `null` or contains no elements, returns an empty array.
2. **Take top failures**: Uses `Take(max)` to select the first N failures.
3. **Map to PostError**: For each failure:- **Code**: `"AIS_VALIDATION_{failure.Code}"`
- **Message**: `"WO={WorkOrderGuid} WO#={WorkOrderNumber} Line={WorkOrderLineGuid} {Message}"`
- Other fields (`StagingId`, `JournalId`, `JournalDeleted`, `DeleteMessage`) are set to `null` or `false`.
4. **Convert to list**: Returns the mapped collection as a `List<PostError>`.

## Example Usage

```csharp
// Assume failures is populated from local or FSCM validation
var conciseErrors = AisValidationPostErrorBuilder.BuildConcisePostErrors(failures, max: 20);

foreach (var err in conciseErrors)
{
    Console.WriteLine($"{err.Code}: {err.Message}");
}
```

## Integration Points

- **Posting Handlers**

After preparing posting payloads, the `PostAndProcessAsync` handler aggregates validation errors using `PostErrorAggregator`. It may call this builder to shape errors before attaching them to a `PostResult` .

- **Email Notifications**

The `ErrorEmailComposer` formats invalid work-order rows; it can leverage concise `PostError` lists to populate email bodies with summary information  .

## Related Domain Models

| Class | Namespace | Role |
| --- | --- | --- |
| **PostError** | `Rpc.AIS.Accrual.Orchestrator.Core.Domain` | Represents a posting-stage error with code, message, and metadata. |
| **WoPayloadValidationFailure** | `Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation` | Holds detailed validation results for a work-order line. |


## Testing Considerations

- **Empty Input**: Verify that passing `null` or an empty failure list returns an empty `PostError` list.
- **Max Limiting**: Supply more than `max` failures and ensure only the first `max` are returned.
- **Message Formatting**: Validate that each `PostError.Message` contains the correct `WorkOrderGuid`, `WorkOrderNumber`, `WorkOrderLineGuid`, and original failure message.
- **Code Prefixing**: Confirm that every output `PostError.Code` starts with `AIS_VALIDATION_`.

---

Continued use of this utility ensures that downstream consumers handle a predictable, concise set of errors, improving readability and system robustness.