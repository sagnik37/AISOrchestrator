# SOLID second-pass refactor notes

This package applies an additional structural refactor on top of the previously shared refactored codebase.

## Scope applied
- Reduced orchestration responsibilities in `CustomerChangeOrchestrator`
  - extracted request parsing into `CustomerChangeRequest.cs`
  - extracted payload read/rewrite helpers into `CustomerChangePayloadReads.cs`
- Split `InvoiceAttributeSyncRunner` into partials
  - orchestration stays in `InvoiceAttributeSyncRunner.cs`
  - payload/header/taxability readers moved to `InvoiceAttributeSyncRunner.Readers.cs`
- Split `WoLocalValidator` into partials
  - high-level flow remains in `WoLocalValidator.cs`
  - line/date/quantity numeric checks moved to `WoLocalValidator.LineValidation.cs`

## SOLID impact
- **SRP**: orchestration classes now delegate parsing and payload mutation concerns to focused collaborators/files.
- **OCP**: validation and reader logic are isolated, making future extension lower-risk.
- **DIP**: no runtime DI behavior was changed in this pass; the refactor is intentionally behavior-preserving.

## Important note
This is still not a complete “every class” strict-SOLID rewrite of the whole repository.  
The next highest-value deep refactor targets remain:
- `DeltaJournalSectionBuilder.cs`
- `DeltaPayloadBuilder.cs`
- `FscmJournalFetchHttpClient.cs`
- `Deprecated/Services/WoDeltaPayloadService.cs`

This pass is designed to be safer for merging because it focuses on decomposition rather than algorithmic changes.
