# WoPayloadValidationFailure Feature Documentation

## Overview

The **WoPayloadValidationFailure** record represents a single validation issue detected during the AIS-side work order (WO) payload validation process. Each instance captures all relevant context: which work order or line failed, why it failed, and how it should be handled. This allows downstream components to:

- Aggregate and report invalid WO lines
- Exclude or retry work orders based on severity
- Fail-fast on critical validation errors

It fits into the broader AIS Orchestrator pipeline, interacting with envelope parsing, local rules, and optional remote validation services to ensure only valid data is posted to FSCM.

## Domain Context

- **Namespace**: `Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation`
- **Used By**:- **WoEnvelopeParser**: emits failures for malformed payload envelopes
- **WoLocalValidator**: records local AIS validation issues (missing/invalid fields)
- **FscmWoPayloadValidationClient**: converts remote FSCM errors into failures
- **Consumed By**:- **WoPayloadValidationResult**: collects all failures for reporting and filtering
- **InvalidPayloadEmailNotifier**: notifies stakeholders of validation failures

## Record Definition

```csharp
public sealed record WoPayloadValidationFailure(
    Guid WorkOrderGuid,
    string? WorkOrderNumber,
    JournalType JournalType,
    Guid? WorkOrderLineGuid,
    string Code,
    string Message,
    ValidationDisposition Disposition);
```

### Property Reference

| Property | Type | Description |
| --- | --- | --- |
| **WorkOrderGuid** | `Guid` | Internal unique identifier of the work order containing the failure. |
| **WorkOrderNumber** | `string?` | External work order reference (e.g., FSA number), if available. |
| **JournalType** | `JournalType` | The category of journal (Item, Expense, or Hour) to which this failure pertains. |
| **WorkOrderLineGuid** | `Guid?` | Identifier of the specific line within the work order, when the failure is line-specific. |
| **Code** | `string` | Machine-readable error code (e.g., `"AIS_LINE_MISSING_GUID"`). |
| **Message** | `string` | Human-readable explanation of the issue. |
| **Disposition** | `ValidationDisposition` | Indicates severity and handling strategy (Valid, Invalid, Retryable, or FailFast). |


## Key Enumerations

- **JournalType**: categorizes failures by journal (Item, Expense, Hour).
- **ValidationDisposition**: instructs the pipeline how to treat each failure:- `Valid`: no issue, may proceed
- `Invalid`: drop and do not retry
- `Retryable`: transient error, may retry
- `FailFast`: abort the entire run immediately

## Usage Example

```csharp
// Creating an invalid-quantity failure for a specific line
var failure = new WoPayloadValidationFailure(
    WorkOrderGuid: Guid.Parse("d2f0f0b2-1a2b-4c3d-9e4f-5a6b7c8d9e0f"),
    WorkOrderNumber: "WO-12345",
    JournalType: JournalType.Expense,
    WorkOrderLineGuid: Guid.NewGuid(),
    Code: "AIS_LINE_MISSING_QUANTITY",
    Message: "Journal line missing required Quantity.",
    Disposition: ValidationDisposition.Invalid
);
```

## Component Integration

- **WoEnvelopeParser** invokes:

```csharp
  new WoPayloadValidationFailure(
    Guid.Empty, null, journalType, null,
    "AIS_PAYLOAD_MISSING_REQUEST",
    "Payload missing _request object.",
    ValidationDisposition.Invalid
  );
```

- **WoLocalValidator** adds line-level failures when required fields are absent or malformed.
- **FscmWoPayloadValidationClient** maps remote FSCM failures into this record for unified handling.

## Key Classes Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| WoPayloadValidationFailure | Core.Domain.Validation/WoPayloadValidationFailure.cs | Captures details of an individual validation failure. |
| ValidationDisposition | Core.Domain.Validation/ValidationDisposition.cs | Enumerates handling strategies for failures. |
| WoPayloadValidationResult | Core.Domain.Validation/WoPayloadValidationResult.cs | Aggregates failures and filtered payload data. |
| FilteredWorkOrder | Core.Domain.Validation/FilteredWorkOrder.cs | Represents payload sections passing validation. |


## Error Handling Pattern

The validation pipeline uniformly collects failures in lists of `WoPayloadValidationFailure`. Post-validation components inspect the `Disposition`:

- **Invalid**: drop work order or line
- **Retryable**: schedule retry logic
- **FailFast**: halt processing immediately

This ensures consistent, deterministic error handling across local and remote validation steps.

## Dependencies

- **JournalType**: identifies the journal section (Item, Expense, Hour).
- **ValidationDisposition**: guides control flow in the validation engine.

All other logic (parsing, logging, aggregation) relies on standardized use of this record to propagate issue context through the orchestrator.