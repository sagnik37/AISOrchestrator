# LogText Utility Documentation

## Overview

The **LogText** class provides lightweight helpers to ensure log messages remain concise and consistent. It offers methods for trimming overly long strings and normalizing GUID representations before writing to logging systems like Application Insights.

## File Location & Namespace

- **File Path:** `src/Rpc.AIS.Accrual.Orchestrator.Application/Features/Shared/Utilities/LogText.cs`
- **Namespace:** `Rpc.AIS.Accrual.Orchestrator.Core.Utilities`

## Purpose

- Ensure log entries do not exceed safe size limits.
- Provide consistent, brace-free GUID string formatting.
- Prevent null or whitespace inputs from polluting logs.

## Class Definition

```csharp
public static class LogText
{
    public static string TrimForLog(string? s, int maxChars = 4000) { … }
    public static string NormalizeGuidString(string? maybeGuid)   { … }
}
```

The class is declared `static` to group stateless utility methods for log-safe text handling.

## Public Methods

### TrimForLog 🔪

Trims or returns an input string up to a specified maximum length, appending an ellipsis when truncated. Guards against null, empty, or non-positive limits.

```csharp
public static string TrimForLog(string? s, int maxChars = 4000)
```

- **Parameters**

| Name | Type | Description | Default |
| --- | --- | --- | --- |
| `s` | `string?` | Input text to trim | — |
| `maxChars` | `int` | Maximum characters allowed in output | 4000 |


- **Returns**- Original string if its length ≤ `maxChars`.
- Truncated substring of length `maxChars` plus `" ..."` if longer.
- Empty string if input is null, whitespace, or `maxChars` ≤ 0.

- **Edge Cases**- Null or whitespace → empty string
- `maxChars` ≤ 0 → empty string

### NormalizeGuidString 🔄

Cleans up a GUID string by trimming whitespace and removing surrounding braces.

```csharp
public static string NormalizeGuidString(string? maybeGuid)
```

- **Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `maybeGuid` | `string?` | GUID text possibly containing braces or spaces |


- **Returns**- Trimmed GUID without leading `{` or trailing `}`.
- Empty string if input is null or whitespace.

- **Edge Cases**- Null or whitespace → empty string
- Strings without braces remain unchanged after trim

## Usage Examples

```csharp
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

// Example 1: Trimming a long message
string longMessage = new string('A', 5000);
string safeLog = LogText.TrimForLog(longMessage, maxChars: 1000);
// safeLog.Length == 1005 (1000 chars + " ...")

// Example 2: Normalizing GUID inputs
string rawGuid1 = "{A0B1C2D3-E4F5-6789-ABCD-0123456789EF}";
string rawGuid2 = "  A0B1C2D3-E4F5-6789-ABCD-0123456789EF  ";
string norm1 = LogText.NormalizeGuidString(rawGuid1); // "A0B1C2D3-E4F5-6789-ABCD-0123456789EF"
string norm2 = LogText.NormalizeGuidString(rawGuid2); // same result
```

## Integration Points

- Employed in logging routines throughout the application to guard against oversized messages.
- Complements other utilities (e.g., JSON payload loggers) to streamline App Insights traces.

## Testing Considerations

Key scenarios to verify correct behavior:

1. **TrimForLog**- Input `null`, `""`, or whitespace → `""`
- `maxChars` ≤ 0 → `""`
- Short string (≤ limit) returns original
- Long string (> limit) returns truncated + `" ..."`

1. **NormalizeGuidString**- Input `null`, `""`, or whitespace → `""`
- GUID with braces → braces removed
- GUID without braces → trimmed only

## Key Class Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| LogText | `src/Rpc.AIS.Accrual.Orchestrator.Application/Features/Shared/Utilities/LogText.cs` | Provide text-trimming and GUID-normalization helpers for logging |


---

*This utility enforces concise, predictable log output, reducing noise and guarding against Application Insights limits.*