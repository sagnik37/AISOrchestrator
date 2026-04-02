# SOLID Fourth Pass Notes

This pass continues the structural refactor on top of v3 and focuses on pushing responsibilities into narrower units without intentionally changing business behavior.

## What changed

### 1. `DeltaJournalSectionBuilder` decomposed further
The remaining `BuildAsync` hot path was split into smaller units:

- `DeltaJournalSectionBuilder.LineResolution.cs`
  - resolves a raw FS line into a `FsaWorkOrderLineSnapshot`
  - centralizes fallback logic from FS to FSCM aggregation
  - isolates source-field interpretation from orchestration

- `DeltaJournalSectionBuilder.LineMutation.cs`
  - builds a planned output line
  - separates payload hygiene, description/reference stamping, date stamping, dimension stamping, and amount stamping

This reduces the orchestration method’s responsibility from “parse + decide + mutate + log + shape payload” to primarily “iterate + calculate + delegate”.

### 2. FSCM row mapping extracted from `FscmJournalFetchHttpClient`
Introduced:

- `IFscmJournalLineRowMapper`
- `FscmJournalLineRowMapper`

The HTTP client now focuses on transport concerns:
- URL construction
- retries/fallbacks
- response classification
- correlation logging

The mapper now owns row-to-domain translation:
- OData `value[]` parsing
- tolerant field extraction
- dimension parsing
- payload snapshot creation
- `FscmJournalLine` construction

This is a stronger SRP/DIP alignment than the previous partial split alone.

## SOLID impact

### SRP
Large multi-purpose methods were narrowed into smaller collaborators and helper units.

### OCP
The row-mapping concern is now easier to extend independently from HTTP fetch behavior.

### DIP
`FscmJournalFetchHttpClient` now depends on `IFscmJournalLineRowMapper` instead of embedding mapping rules directly.

## Remaining high-value candidates

Next wave should target the remaining orchestration-heavy and translation-heavy areas, especially:

- `FsaDeltaPayloadUseCase.cs`
- `FsaDeltaPayloadJsonInjector.cs`
- `IFscmJournalPoster.cs` / posting workflow pipeline
- `JobOperationsUseCaseBase.cs`
- `ValidateAndPostWoPayloadHandler.cs`

## Validation note

This refactor was prepared as a structural code change package. A full `dotnet build` / test run still needs to be executed in the target development environment.
