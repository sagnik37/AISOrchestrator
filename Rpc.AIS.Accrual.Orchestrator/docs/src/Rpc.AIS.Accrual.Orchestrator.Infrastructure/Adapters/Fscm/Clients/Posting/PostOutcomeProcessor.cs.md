# FSCM Posting Adapter Feature Documentation

## Overview

The **FSCM Posting Adapter** implements the end-to-end workflow for posting Item, Expense, and Hour journal entries to an external FSCM (Financial Supply Chain Management) service. It consists of:

- A **preparation pipeline** that normalizes, validates, projects, and enriches a Work Order payload.
- An **HTTP posting component** that handles multi-step calls (Validate, Create, Post) against FSCM endpoints.
- A **result processor** that maps HTTP responses into a uniform `PostResult`, aggregates errors, and invokes extensibility handlers.

This feature ensures reliable, traceable, and extensible posting of accrual data to FSCM while isolating HTTP details from business logic.

## Architecture Overview

```mermaid
flowchart TB
  subgraph Preparation
    Prep[WoPostingPreparationPipeline]
  end

  subgraph PostingWorkflow
    Prep --> Outcome[PostOutcomeProcessor]
    Outcome --> Poster[FscmJournalPoster]
    Poster --> HttpClient[HttpClient]
    HttpClient --> Poster
    Poster --> Outcome
  end

  subgraph Support
    Parser[FscmPostingResponseParserAdapter]
    ErrorAgg[PostErrorAggregator]
    Handlers[IPostResultHandler(s)]
    RequestFactory[FscmPostRequestFactory]
    DTOs[FscmPostJournalRequest DTOs]
  end

  Outcome --> Parser
  Outcome --> ErrorAgg
  Outcome --> Handlers
  Poster --> RequestFactory
  Poster --> DTOs
```

## Component Structure

### Interfaces

#### **IPostOutcomeProcessor**

Location: `src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/Posting/IPostOutcomeProcessor.cs`

Defines the contract for executing a post and converting the HTTP response into a `PostResult`, including running any registered `IPostResultHandler`s.

| Method | Description | Returns |
| --- | --- | --- |
| PostAndProcessAsync(RunContext, PreparedWoPosting, CancellationToken) | Executes the HTTP post, aggregates errors, parses response, returns a `PostResult`. | `Task<PostResult>` |


#### **IPostErrorAggregator**

Location: `src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/Posting/IPostErrorAggregator.cs`

Aggregates validation/pre errors, section-pruning alerts, HTTP errors, and parse errors into a unified list of `PostError`.

| Method | Description | Returns |
| --- | --- | --- |
| Build(IReadOnlyList<PostError>, int removed, JournalType) | Builds initial error list, adds a `WO_SECTION_PRUNED` error if work orders were pruned. | `List<PostError>` |
| AddHttpError(List<PostError>, HttpPostOutcome) | Adds an HTTP error entry if status code is not 2xx. | `List<PostError>` |
| AddParseErrors(List<PostError>, IReadOnlyList<PostError>) | Appends parse errors to the list. | `List<PostError>` |


#### **IFscmPostingResponseParser**

Location: `src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/Posting/IFscmPostingResponseParser.cs`

Parses the raw FSCM response body JSON into a `ParseOutcome`.

| Method | Description | Returns |
| --- | --- | --- |
| Parse(string body) | Executes parse logic and returns outcome. | `ParseOutcome` |


#### **IFscmJournalPoster**

Location: `src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/Posting/IFscmJournalPoster.cs`

Sends HTTP POST calls to FSCM endpoints for a given `JournalType` and payload JSON.

| Method | Description | Returns |
| --- | --- | --- |
| PostAsync(RunContext, JournalType, string payloadJson, CancellationToken) | Executes the FSCM HTTP step(s). | `Task<HttpPostOutcome>` |


#### **IWoPostingPreparationPipeline**

Location: `src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/Posting/IWoPostingPreparationPipeline.cs`

Prepares a Work Order payload for a single journal type through normalization, shape-guard, local/remote validation, projection, and date adjustment.

| Method | Description | Returns |
| --- | --- | --- |
| PrepareAsync(RunContext, JournalType, string payload, CancellationToken) | Full prepare pipeline. | `Task<PreparedWoPosting>` |
| PrepareValidatedAsync(RunContext, JournalType, string payload, string? validationRaw, CancellationToken) | Prepare when remote validation already ran. | `Task<PreparedWoPosting>` |


#### **IPostResultHandler**

Location: `src/Rpc.AIS.Accrual.Orchestrator.Core/Abstractions/IPostResultHandler.cs`

Extensibility point invoked after posting attempts. Handlers can implement notifications, compensations, or custom retries.

| Method | Description | Returns |
| --- | --- | --- |
| CanHandle(PostResult result) | Determines if this handler should run. | `bool` |
| HandleAsync(RunContext, PostResult, CancellationToken) | Performs handler’s post-processing action. | `Task` |


### Implementations

#### **PostOutcomeProcessor** ✨

Location: `src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/Posting/PostOutcomeProcessor.cs`

The default `IPostOutcomeProcessor` that:

1. Invokes `IFscmJournalPoster.PostAsync`
2. Aggregates pre-errors and pruning alerts via `IPostErrorAggregator`
3. Maps HTTP errors (non-2xx) to `PostError`
4. Parses success responses via `IFscmPostingResponseParser`
5. Honors skip triggers (`Timer`, `AdHocSingle`, `AdHocAll`, `AdHocBulk`) to skip actual posting when appropriate
6. Builds a `PostResult` and invokes all applicable `IPostResultHandler`s

```csharp
public async Task<PostResult> PostAndProcessAsync(RunContext ctx, PreparedWoPosting prepared, CancellationToken ct)
{
    var outcome = await _poster.PostAsync(ctx, prepared.JournalType, prepared.ProjectedJournalPayloadJson, ct);
    var errors = _errorAgg.Build(prepared.PreErrors, prepared.RemovedDueToMissingOrEmptySection, prepared.JournalType);

    if ((int)outcome.StatusCode is < 200 or > 299)
    {
        var all = _errorAgg.AddHttpError(errors, outcome);
        var fail = new PostResult(...);
        await RunHandlersAsync(ctx, fail, ct);
        return fail;
    }

    var parsed = _parser.Parse(outcome.Body ?? string.Empty);
    if (!parsed.Ok)
    {
        var all = _errorAgg.AddParseErrors(errors, parsed.ParseErrors);
        var fail = new PostResult(...);
        await RunHandlersAsync(ctx, fail, ct);
        return fail;
    }

    var skip = SkipJournalPostingTriggers.Contains(ctx.TriggeredBy ?? "");
    var ok = new PostResult(... skipPosting: skip);
    await RunHandlersAsync(ctx, ok, ct);
    return ok;
}
```

#### **PostErrorAggregator**

> **Skip Triggers**: If `ctx.TriggeredBy` equals any of `Timer`, `AdHocSingle`, `AdHocAll`, or `AdHocBulk`, actual journal posting is skipped in the success case.

Location: `src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/Posting/PostErrorAggregator.cs`

Implements `IPostErrorAggregator` by consolidating:

- **Validation & Pruning**: `WO_SECTION_PRUNED` errors when sections were missing.
- **HTTP Errors**: Adds `FSCM_POST_HTTP_ERROR` for non-2xx status codes.
- **Parse Errors**: Appends errors from `ParseOutcome.ParseErrors`.

#### **FscmPostingResponseParserAdapter**

Location: `src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/Posting/FscmPostingResponseParserAdapter.cs`

Wraps `Infrastructure.Utilities.FscmPostingResponseParser.TryParse` and maps its tuple into `ParseOutcome`.

#### **FscmJournalPoster**

Location: `src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/Posting/FscmJournalPoster.cs`

Executes the multi-step FSCM posting flow:

1. **Validate** endpoint
2. **Create** journal entries
3. **Post** the created journals in batch

It extracts journal IDs from the create response and constructs a `FscmPostJournalRequest`.

#### **PostingHttpClientWorkflow**

Location: `src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/Posting/PostingHttpClientWorkflow.cs`

Coordinates:

- **Preparation** via `IWoPostingPreparationPipeline`
- **Posting** via `IPostOutcomeProcessor`

Key methods:

- `PostAsync(...)`, `PostFromWoPayloadAsync(...)`, `PostValidatedWoPayloadAsync(...)`, `ValidateOnceAndPostAllJournalTypesAsync(...)`

#### **WoPostingPreparationPipeline**

Location: `src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/Posting/WoPostingPreparationPipeline.cs`

Implements `IWoPostingPreparationPipeline`:

1. Normalizes raw WO JSON to a standardized structure
2. Injects journal names if missing
3. Optionally fetches missing operation dates from FSA
4. Executes local validation/filter, notifies invalid payloads
5. Projects payload to a single journal type
6. Adjusts dates in the projected payload
7. Returns a `PreparedWoPosting` with retryable counts

#### **FscmPostRequestFactory**

Location: `src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/Posting/FscmPostRequestFactory.cs`

Creates JSON POST requests with:

- `Content-Type: application/json`
- Headers `x-run-id`, `x-correlation-id` from `RunContext`

#### **FscmJournalPostDtos**

Location: `src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/Posting/FscmJournalPostDtos.cs`

DTOs for the FSCM “post journal” endpoint:

- `FscmPostJournalRequest`
- `FscmPostJournalEnvelope`
- `FscmJournalPostItem`

#### **PostingWorkflowFactory**

Location: `src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/PostingWorkflowFactory.cs`

Composes all dependencies—poster, parser, error aggregator, handlers, pipelines—into a single `PostingHttpClientWorkflow`.

### Delta Payload Enrichment

> In addition to posting, the repository contains an **FSA Delta Payload Enrichment pipeline**:

- **EnrichmentContext** (…/EnrichmentContext.cs): Immutable bundle of payload JSON and auxiliary lookup maps.
- **IFsaDeltaPayloadEnrichmentPipeline** & **IFsaDeltaPayloadEnrichmentStep** (…/EnrichmentPipeline): Defines pipeline and steps for enriching delta payloads.
- **CompanyEnrichmentStep**: Injects company names into the payload based on context.

## Key Classes Reference

| Class | Path | Responsibility |
| --- | --- | --- |
| PostOutcomeProcessor | src/.../Clients/Posting/PostOutcomeProcessor.cs | Executes HTTP post, aggregates errors, parses response, invokes result handlers |
| IPostOutcomeProcessor | src/.../Clients/Posting/IPostOutcomeProcessor.cs | Contract for outcome processing |
| PostErrorAggregator | src/.../Clients/Posting/PostErrorAggregator.cs | Aggregates validation, HTTP, and parse errors |
| IFscmPostingResponseParser | src/.../Clients/Posting/IFscmPostingResponseParser.cs | Parses FSCM response JSON into `ParseOutcome` |
| FscmPostingResponseParserAdapter | src/.../Clients/Posting/FscmPostingResponseParserAdapter.cs | Adapter implementation of response parser |
| IFscmJournalPoster | src/.../Clients/Posting/IFscmJournalPoster.cs | Posts JSON payloads to FSCM endpoints |
| PostingHttpClientWorkflow | src/.../Clients/Posting/PostingHttpClientWorkflow.cs | Orchestrates preparation + outcome processing |
| IWoPostingPreparationPipeline | src/.../Clients/Posting/IWoPostingPreparationPipeline.cs | Contract for work order payload preparation |
| WoPostingPreparationPipeline | src/.../Clients/Posting/WoPostingPreparationPipeline.cs | Implements payload normalization, validation, projection, and date adjustment |
| FscmPostRequestFactory | src/.../Clients/Posting/FscmPostRequestFactory.cs | Builds HTTP request messages for FSCM |
| FscmPostJournalRequest | src/.../Clients/Posting/FscmJournalPostDtos.cs | DTOs representing FSCM post-journal envelope |
| PostingWorkflowFactory | src/.../Clients/PostingWorkflowFactory.cs | Factory composes the posting workflow |
| EnrichmentContext | src/.../EnrichmentPipeline/EnrichmentContext.cs | Input bundle for FSA delta payload enrichment |
| IFsaDeltaPayloadEnrichmentPipeline | src/.../EnrichmentPipeline/IFsaDeltaPayloadEnrichmentPipeline.cs | Pipeline orchestrator for delta enrichment |
| IFsaDeltaPayloadEnrichmentStep | src/.../EnrichmentPipeline/IFsaDeltaPayloadEnrichmentStep.cs | Single enrichment step interface |
| CompanyEnrichmentStep | src/.../EnrichmentPipeline/Steps/CompanyEnrichmentStep.cs | Injects company names into outbound payload |


## Error Handling

- **Validation & Pruning**: Pre-errors from `PreparedWoPosting.PreErrors` and pruning alerts (`WO_SECTION_PRUNED`).
- **HTTP Errors**: Any non-2xx status adds a `PostError` with code `FSCM_POST_HTTP_ERROR`.
- **Parse Errors**: Failures in response parsing are appended via `AddParseErrors`.
- **Skip Posting**: Entries are not sent when `RunContext.TriggeredBy` equals `Timer`, `AdHocSingle`, `AdHocAll`, or `AdHocBulk`.

## Testing Considerations

- **Success Path**: 2xx HTTP response, valid parse → `PostResult.IsSuccess = true`.
- **HTTP Failure**: 4xx/5xx response → error aggregated, `IsSuccess = false`.
- **Parse Failure**: Malformed JSON → parse errors aggregated.
- **Skip Trigger**: Setting `ctx.TriggeredBy` to a skip value → posting skipped but treated as success.
- **Handler Exception**: Simulate a handler throwing to ensure it's logged and does not disrupt the main flow.