# FSCM Accounting Period Resolver – Parsing Helpers 📑

## Overview

The **Parsing Helpers** within the `FscmAccountingPeriodResolver` centralize JSON‐related utilities. They parse OData responses, extract values, and provide low-level transformations for dates, strings, and keys. This ensures consistent, robust handling of FSCM API payloads and aids in building higher-level domain models.

## Component Structure

### Data Access Layer – Parsing Helpers

**File:** `src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/FscmAccountingPeriodResolver.Parsing.cs`

- **Purpose:** Support the main resolver by parsing JSON, extracting properties, and preparing data for business logic.
- **Responsibilities:**- Validate and parse raw JSON into `JsonElement` arrays
- Safely extract typed values (dates, strings, booleans)
- Provide utility routines for OData formatting and internal caching

## Parsing Methods

| Method | Description |
| --- | --- |
| **ParseArray**(string json, string stepForLogs) | Parses a JSON string; extracts the `value` array. Logs warnings for missing/invalid structures and errors for parse failures. |
| **TryGetDateTimeUtc**(JsonElement obj, string propName) | Attempts to read a UTC `DateTime` from a JSON string property using ISO or fallback parsing. Returns `null` if absent or unparseable. |
| **TryGetString**(JsonElement obj, string propName) | Reads a property as string, number, or boolean. Returns `null` for other kinds or missing properties. |
| **CountMissingStatus**(List<PeriodRow> periods, IReadOnlyDictionary<string,string?> statuses) | Tallies how many periods lack a corresponding status entry in the provided dictionary. |
| **ToODataDateTimeOffsetLiteralUnquoted**(DateTime utcDateTime) | Formats a UTC `DateTime` as an OData literal (e.g., `2023-05-01T00:00:00Z`) without surrounding quotes. |
| **EscapeODataString**(string s) | Escapes single quotes by doubling them (`'` → `''`) for safe OData query embedding. |
| **Trim**(string? s) | Truncates strings to a 4000-character maximum and removes leading/trailing whitespace. Returns empty for null/whitespace input. |
| **PeriodKey**(string calendar, string fiscalYear, string periodName) | Builds a composite key in the format `calendar | fiscalYear | periodName` for period identification. |


## Data Models

### PeriodRow

Carries a single fiscal period’s metadata for internal processing.

```csharp
private sealed record PeriodRow(
    string Calendar,
    string FiscalYear,
    string PeriodName,
    DateTime StartDate,
    DateTime EndDate)
{
    public string Key => PeriodKey(Calendar, FiscalYear, PeriodName);
}
```

| Property | Type | Description |
| --- | --- | --- |
| Calendar | string | Identifier of the FSCM calendar (e.g. “Fis Cal”). |
| FiscalYear | string | Year label (e.g. “2023”). |
| PeriodName | string | Period label within the year (e.g. “01”). |
| StartDate | DateTime | UTC start date of the period. |
| EndDate | DateTime | UTC end date of the period. |
| Key | string | Read-only composite key used for lookups. |


## Logging Strategy

- **Warnings** for empty or malformed JSON, missing `value` arrays, or unexpected data kinds.
- **Information** on successful parsing counts.
- **Errors** on JSON exceptions, including a trimmed body preview.

## Example Usage

```csharp
// Within FscmAccountingPeriodResolver
var jsonResponse = await SendODataAsync(context, "FSCM.Periods", url, ct);
var rows = ParseArray(jsonResponse, "FSCM.Periods");
foreach (var element in rows)
{
    var periodName = TryGetString(element, "PeriodName");
    var start      = TryGetDateTimeUtc(element, "StartDate");
    // ...
}
```

## Dependencies

- System.Text.Json
- Microsoft.Extensions.Logging
- Rpc.AIS.Accrual.Orchestrator.Core.Domain (`RunContext`, domain models)
- Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options (configuration values)

---

*End of Parsing Helpers Documentation*