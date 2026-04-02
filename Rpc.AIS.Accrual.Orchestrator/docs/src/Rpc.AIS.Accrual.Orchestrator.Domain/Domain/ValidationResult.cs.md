# ValidationResult Domain Model Documentation

## Overview 🎯

The **ValidationResult** record encapsulates the outcome of validating a single accrual staging reference. It indicates whether a record passed or failed validation and carries optional error details when invalid . This model is used throughout the accrual orchestration pipeline to collect per-record validation statuses.

## Class Definition

```csharp
namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain;

/// <summary>
/// Carries validation result data.
/// </summary>
public sealed record ValidationResult(
    AccrualStagingRef Record,
    bool IsValid,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static ValidationResult Valid(AccrualStagingRef record)
        => new(record, true, null, null);

    public static ValidationResult Invalid(
        AccrualStagingRef record,
        string errorCode,
        string errorMessage)
        => new(record, false, errorCode, errorMessage);
}
```

This record provides a self-documenting API for creating valid or invalid results .

## Properties

| Property | Type | Description |
| --- | --- | --- |
| **Record** | AccrualStagingRef | Reference to the staging record under validation. |
| **IsValid** | bool | `true` if validation succeeded; `false` otherwise. |
| **ErrorCode** | string? | Machine-readable code when invalid. |
| **ErrorMessage** | string? | Human-readable explanation when invalid. |


## Factory Methods

- **Valid(AccrualStagingRef record)**

Returns a `ValidationResult` marked valid, with no error details.

- **Invalid(AccrualStagingRef record, string errorCode, string errorMessage)**

Returns a `ValidationResult` marked invalid, populating error code and message.

## Relationships

- **AccrualStagingRef**: Identifies the record being validated.

```csharp
  public sealed record AccrualStagingRef(
      string StagingId,
      JournalType JournalType,
      string SourceKey);
```

- **RunOutcome**: Aggregates `ValidationResult` instances in its `ValidationFailures` list to summarize run-level validation .

```csharp
  public sealed record RunOutcome(
      RunContext Context,
      int StagingRecordsConsidered,
      int ValidRecords,
      int InvalidRecords,
      IReadOnlyList<PostResult> PostResults,
      IReadOnlyList<ValidationResult> ValidationFailures,
      IReadOnlyList<string> GeneralErrors,
      int WorkOrdersConsidered = 0,
      int WorkOrdersValid = 0,
      int WorkOrdersInvalid = 0)
  {
      public bool HasAnyErrors => 
          InvalidRecords > 0
          || WorkOrdersInvalid > 0
          || PostResults.Any(r => !r.IsSuccess)
          || GeneralErrors.Count > 0;
  }
```

## Usage Example

```csharp
var stagingRef = new AccrualStagingRef("ABC123", JournalType.Item, "SRC01");

// Mark as valid
var resultOk = ValidationResult.Valid(stagingRef);

// Mark as invalid with details
var resultErr = ValidationResult.Invalid(
    stagingRef,
    "VAL_MISSING_FIELD",
    "Quantity is required.");
```

## Key Classes Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| **ValidationResult** | src/Rpc.AIS.Accrual.Orchestrator.Domain/Domain/ValidationResult.cs | Represents validation outcome for a staging record. |
| **AccrualStagingRef** | src/Rpc.AIS.Accrual.Orchestrator.Domain/Domain/AccrualStagingRef.cs | Holds reference data for the staging record. |
| **RunOutcome** | src/Rpc.AIS.Accrual.Orchestrator.Domain/Domain/RunOutcome.cs | Summarizes orchestration run, including failures. |


## Testing Scenarios

- Verify **Valid(...)** produces `IsValid == true` and null error details.
- Verify **Invalid(...)** produces `IsValid == false` with provided `ErrorCode` and `ErrorMessage`.
- Confirm that `ValidationResult` equality is value-based (record semantics).