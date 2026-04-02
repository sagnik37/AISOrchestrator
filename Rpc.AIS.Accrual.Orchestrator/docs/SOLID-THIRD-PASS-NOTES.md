# SOLID Refactor - Third Pass

This pass continues the safe structural refactor on top of the v2 package.

## Scope

Focused on large classes that still mixed multiple responsibilities:

- `DeltaJournalSectionBuilder`
- `DeltaPayloadBuilder`
- `FscmJournalFetchHttpClient`
- `WoDeltaPayloadService`

## Changes applied

### 1. DeltaJournalSectionBuilder
Split into partial files by concern:
- `DeltaJournalSectionBuilder.cs` -> orchestration / build flow
- `DeltaJournalSectionBuilder.Helpers.cs` -> JSON lookup, dimension/date parsing, common helpers
- `DeltaJournalSectionBuilder.Reversal.cs` -> reversal-line construction and payload mutation helpers

### 2. DeltaPayloadBuilder
Split into partial files by concern:
- `DeltaPayloadBuilder.cs` -> payload/work-order orchestration
- `DeltaPayloadBuilder.Journals.cs` -> item/expense/hour journal builders
- `DeltaPayloadBuilder.Reflection.cs` -> reflection-based property access helpers

### 3. FscmJournalFetchHttpClient
Split into partial files by concern:
- `FscmJournalFetchHttpClient.cs` -> public entrypoint and batching logic
- `FscmJournalFetchHttpClient.Http.cs` -> HTTP execution, retry/fallback behavior
- `FscmJournalFetchHttpClient.Parsing.cs` -> OData parsing and primitive readers

### 4. WoDeltaPayloadService
Kept the main delta orchestration intact and extracted static support helpers into:
- `WoDeltaPayloadService.Helpers.cs`

## Intent

This pass is primarily SRP-oriented and designed to make the next wave of deeper refactors safer:
- isolate orchestration from parsing/mapping helpers
- reduce god-file size
- improve code navigation for future dependency inversion work

## Important note

This is still a behavior-preserving structural pass. It should be validated with a local `dotnet build` and the existing test suite in your environment before merge/deployment.
