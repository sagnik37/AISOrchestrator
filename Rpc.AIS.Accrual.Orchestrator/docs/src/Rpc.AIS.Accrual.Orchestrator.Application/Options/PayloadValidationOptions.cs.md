# PayloadValidationOptions Feature Documentation

## Overview

The **PayloadValidationOptions** class centralizes AIS-side payload validation settings. It lets you control whether invalid journal lines cause entire work orders to be dropped, enable or disable remote FSCM validation, and configure retry behavior for transient validation failures. By tuning these options, you ensure that only valid, contract-compliant data is posted to downstream systems.

## Configuration Section

Payload validation options are bound from the `"Validation"` section in configuration.

```csharp
public const string SectionName = "Validation";
```

This constant defines the key under which options are loaded via the ASP.NET Core Options pattern .

## 🔧 Properties

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| **DropWholeWorkOrderOnAnyInvalidLine** | `bool` | `true` | If **true**, any invalid journal line excludes the entire work order from the postable payload. |
| **EnableFscmCustomEndpointValidation** | `bool` | `false` | When **true**, calls the FSCM custom validation endpoint after local AIS validation. |
| **FailClosedOnFscmCustomValidationError** | `bool` | `true` | If **true**, failures invoking the FSCM endpoint trigger a fail-fast stop; otherwise, such failures are treated as retryable. |
| **RetryMaxAttempts** | `int` | `3` | Maximum retry attempts for retryable validation failures per journal type within a single orchestration run. |
| **RetryDelaysMinutes** | `int[]` | `[5, 15, 30]` | Delay intervals (in minutes) between retry attempts (1-based). If fewer entries than attempts, the last value is reused. |


*Cited from class definition .*

## Example Configuration

```json
{
  "Validation": {
    "DropWholeWorkOrderOnAnyInvalidLine": true,
    "EnableFscmCustomEndpointValidation": false,
    "FailClosedOnFscmCustomValidationError": true,
    "RetryMaxAttempts": 3,
    "RetryDelaysMinutes": [5, 15, 30]
  }
}
```

## 🚀 Integration Points

These options are injected into core validation components:

- **WoLocalValidator**

Uses `DropWholeWorkOrderOnAnyInvalidLine` to determine if any invalid line should drop the entire work order .

- **FscmCustomValidationClient**

Reads policy values from `PayloadValidationOptions` before making HTTP calls to the FSCM validation endpoint .

- **FscmReferenceValidator**

Checks `EnableFscmCustomEndpointValidation` and `FailClosedOnFscmCustomValidationError` to drive remote validation logic and fail-fast behavior .

- **WoValidationResultBuilder**

Logs summary metrics, including whether FSCM custom validation was enabled and whether whole work-orders are dropped on validation errors .

## 📄 Class Definition

```csharp
namespace Rpc.AIS.Accrual.Orchestrator.Core.Options;

/// <summary>
/// Controls AIS-side payload validation behavior.
/// </summary>
public sealed class PayloadValidationOptions
{
    public const string SectionName = "Validation";

    /// <summary>
    /// If true (default), any invalid journal line causes the entire Work Order
    /// to be excluded from the postable payload for that journal type.
    /// </summary>
    public bool DropWholeWorkOrderOnAnyInvalidLine { get; set; } = true;

    /// <summary>
    /// Enables calling the FSCM custom validation endpoint AFTER local AIS validation.
    /// This should be enabled when FSCM provides a dedicated validation API to validate the AIS payload contract.
    /// </summary>
    public bool EnableFscmCustomEndpointValidation { get; set; }

    /// <summary>
    /// If true (default), failures calling the FSCM custom validation endpoint will fail-closed:
    /// AIS will stop the run (FailFast) rather than posting potentially invalid data.
    /// If false, remote validation call failures will be treated as retryable for the affected work orders.
    /// </summary>
    public bool FailClosedOnFscmCustomValidationError { get; set; } = true;

    /// <summary>
    /// Maximum attempts for retryable validation failures (per journal type) within one orchestration run.
    /// </summary>
    public int RetryMaxAttempts { get; set; } = 3;

    /// <summary>
    /// Retry delays in minutes for attempts 1..N (1-based). If there are fewer entries than attempts,
    /// the last delay value is reused.
    /// </summary>
    public int[] RetryDelaysMinutes { get; set; } = new[] { 5, 15, 30 };
}
```

*Cited class definition .*