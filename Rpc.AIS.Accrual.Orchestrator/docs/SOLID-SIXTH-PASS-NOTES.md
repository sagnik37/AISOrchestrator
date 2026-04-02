# SOLID Sixth Pass Notes

This pass continues the non-behavioral refactor approach and focuses on reducing class-level responsibility concentration in the remaining orchestration and posting hotspots.

## Scope covered

### 1) `FsaDeltaPayloadJsonInjector`
- converted to a `partial` static type
- extracted line-level date/tax injection logic into:
  - `FsaDeltaPayloadJsonInjector.LineInjection.cs`

Result:
- root/request/work-order traversal remains separate from journal-line enrichment logic
- line-guid detection, canonical date stamping, and blank-value replacement are isolated

### 2) `ActivitiesUseCase`
- converted to a `partial` class
- extracted aggregation and payload counting helpers into:
  - `ActivitiesUseCase.Aggregation.cs`

Result:
- the main class remains a thin application coordinator
- helper logic for email aggregation and WO counting is isolated

### 3) `PostRetryableWoPayloadHandler`
- introduced `IRetryableWoPayloadPoster`
- added `RetryableWoPayloadPoster`
- extracted logging/scope helpers into:
  - `PostRetryableWoPayloadHandler.Logging.cs`

Result:
- handler responsibility shifts closer to orchestration + exception boundary handling
- posting execution becomes dependency-inverted and independently replaceable/testable

### 4) `JobOperationsHttpHandlerCommon`
- converted to a `partial` class
- extracted dedicated “unsupported route” response creation into:
  - `JobOperationsHttpFunctions.Response.cs`

Result:
- endpoint routing facade stays focused on delegation
- response construction logic is isolated

### 5) `FscmJournalPoster`
- converted to a `partial` class
- extracted helper concerns into:
  - `FscmJournalPoster.Context.cs`
  - `FscmJournalPoster.Http.cs`
  - `FscmJournalPoster.ResponseParsing.cs`

Result:
- posting workflow orchestration remains in the main file
- payload identity extraction, HTTP execution/logging, endpoint validation, and create-response parsing are separated by concern

### 6) `WoPostingPreparationPipeline`
- converted to a `partial` class
- extracted helper concerns into:
  - `WoPostingPreparationPipeline.JournalNames.cs`
  - `WoPostingPreparationPipeline.OperationsDateEnrichment.cs`
  - `WoPostingPreparationPipeline.Errors.cs`

Result:
- main preparation flow is easier to reason about
- journal-name enrichment, FS operation-date backfill, and retry/error shaping are separated

## Intended SOLID gains
- **SRP**: large classes now have clearer sub-responsibilities
- **DIP**: retryable posting moved behind `IRetryableWoPayloadPoster`
- **OCP**: extension by partial/helper file is easier without modifying the main orchestration file each time
- **Maintainability**: debugging and future unit-test isolation become easier around FSCM posting and retry flows

## Remaining next-pass candidates
- `FscmInvoiceAttributesHttpClient`
- `FsaLineFetcherWorkflow.Public.cs` / helpers
- `WoDeltaPayloadService` (deprecated path cleanup / adapter isolation)
- `FsaDeltaPayloadEnricher.WoHeader.cs`
- any remaining classes > 350 LOC that still mix traversal, mapping, transport, and policy decisions
