# LegalEntityJournalNames Domain Model

## Overview

`LegalEntityJournalNames` is an immutable record that encapsulates the journal name identifiers configured per legal entity in FSCM. These identifiers—Expense, Hour, and Invent—are sourced from the `LegalEntityIntegrationParametersBaseIntParamTables` OData entity set. The model enables downstream services to inject the correct journal names into posting payloads, ensuring accurate journal assignment during accrual processing.

## Domain Model Structure

### LegalEntityJournalNames

**Path:** `src/Rpc.AIS.Accrual.Orchestrator.Domain/Domain/LegalEntityJournalNames.cs`

- **Type:** `sealed record`
- **Purpose:** Holds the three FSCM journal name IDs for a single legal entity.
- **Source Data:** Fetched by the `IFscmLegalEntityIntegrationParametersClient` from FSCM OData .

```csharp
public sealed record LegalEntityJournalNames(
    string? ExpenseJournalNameId,
    string? HourJournalNameId,
    string? InventJournalNameId);
```

## Properties

| Property | Type | Description |
| --- | --- | --- |
| **ExpenseJournalNameId** | `string?` | The configured Expense journal identifier for the entity. |
| **HourJournalNameId** | `string?` | The configured Hour journal identifier for the entity. |
| **InventJournalNameId** | `string?` | The configured Invent (Item) journal identifier. |


## Usage and Integration

- **Fetching Journal Names**

The application layer uses `IFscmLegalEntityIntegrationParametersClient.GetJournalNamesAsync` to retrieve a `LegalEntityJournalNames` instance per company .

- **Client Implementation**

`FscmLegalEntityIntegrationParametersHttpClient` constructs this record by querying FSCM’s OData endpoint and mapping returned JSON properties into the three fields.

- **Payload Enrichment**

`IJournalNamesInjector.InjectJournalNamesIntoPayload` accepts a `Dictionary<string, LegalEntityJournalNames>` to inject the correct journal names into the JSON payload before posting .

- **Enrichment Pipeline Step**

`JournalNamesEnrichmentStep` applies the injector as part of the delta payload enrichment pipeline, ensuring journal sections carry the right names.

## Integration Points

- **Core Abstractions**:- `IFscmLegalEntityIntegrationParametersClient` defines the contract for fetching these parameters.
- `IJournalNamesInjector` drives JSON mutation based on this model.

- **Infrastructure**:- The HTTP client implementation queries FSCM and maps responses into `LegalEntityJournalNames`.
- Dependency injection binds this implementation to the core interface for use across posting workflows.

## Dependencies

- **FSCM OData**: Relies on the `LegalEntityIntegrationParametersBaseIntParamTables` entity set.
- **Core Domain Namespace**: Defined within `Rpc.AIS.Accrual.Orchestrator.Core.Domain`.

## Key Notes

```card
{
    "title": "Nullable Properties",
    "content": "Each journal name identifier is nullable to handle missing configurations gracefully."
}
```