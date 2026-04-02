# FSCM Journal Fetch Policies Feature Documentation

## Overview

The **FSCM Journal Fetch Policies** define how to retrieve and normalize journal lines (Item, Expense, Hour) from the FSCM OData service for accrual processing. By encapsulating metadata (entity set, $select fields) and mapping logic (quantity, unit price extraction), these policies enable the HTTP fetch client to build type-specific OData queries and transform JSON rows into a unified domain model. This design adheres to the Open/Closed Principle: new journal types can be added by implementing `IFscmJournalFetchPolicy` without modifying the fetch client .

In the broader accrual orchestrator, the HTTP client (`FscmJournalFetchHttpClient`) uses the `FscmJournalFetchPolicyResolver` to select the correct policy for a given `JournalType`. It batches work order GUIDs, constructs OData filters, handles transient and missing‐field errors via a fallback $select, and finally parses the JSON into `FscmJournalLine` DTOs for downstream delta calculations.

## Architecture Overview

```mermaid
flowchart TB
  subgraph DataAccessLayer
    FJC[FscmJournalFetchHttpClient]
    RSL[FscmJournalFetchPolicyResolver]
    PIF[IFscmJournalFetchPolicy]
    BASE[FscmJournalFetchPolicyBase]
    ITM[ItemJournalFetchPolicy]
    EXP[ExpenseJournalFetchPolicy]
    HOUR[HourJournalFetchPolicy]
  end

  External[FSCM OData Service]

  FJC -->|resolve policy| RSL
  RSL -->|implements| PIF
  PIF <|-- BASE
  BASE <|-- ITM
  BASE <|-- EXP
  BASE <|-- HOUR
  FJC -->|calls| External
```

## Component Structure

### Data Access Layer

#### **IFscmJournalFetchPolicy** (`src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/FscmJournalPolicies/IFscmJournalFetchPolicy.cs`)

- **Purpose:** Contract for journal-type metadata and mapping rules .
- **Members:- `JournalType JournalType { get; }`
- `string EntitySet { get; }`
- `string Select { get; }`
- `decimal GetQuantity(JsonElement row)`
- `decimal? GetUnitPrice(JsonElement row)`

#### **FscmJournalFetchPolicyBase** (`src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/FscmJournalPolicies/FscmJournalFetchPolicyBase.cs`)

- **Purpose:** Abstract base providing shared logic and fallback support .
- **Members:**- `virtual string SelectFallback => Select`
- `protected static decimal? TryGetDecimal(JsonElement obj, string propName)`

#### **ItemJournalFetchPolicy** (`src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/FscmJournalPolicies/ItemJournalFetchPolicy.cs`)

- **Purpose:** Policy for **Item** journals (`JournalType.Item`) .
- **EntitySet:** `ProjectItemJournalTrans`
- **Select Fields:**- Core: `RPCWorkOrderGuid`,`RPCWorkOrderLineGuid`,`Quantity`,`ProjectSalesPrice`,…
- Extensions: `Amount`,`RPCFSAUnitPrice`,etc.
- **SelectFallback:** Removes `RPCFSAMarkupAmt` when unsupported.
- **GetQuantity:** Tries `"Quantity"` then `"Qty"`.
- **GetUnitPrice:**- Normalizes when `ProjectSalesPrice` represents an extended amount by comparing to `Amount`.
- Falls back to treating `ProjectSalesPrice` as unit if inference fails.

#### **ExpenseJournalFetchPolicy** (`src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/FscmJournalPolicies/ExpenseJournalFetchPolicy.cs`)

- **Purpose:** Policy for **Expense** journals (`JournalType.Expense`) .
- **EntitySet:** `ExpenseJournalLines`
- **Select Fields:** Core expense fields, optional `Amount`, avoids known‐problematic metadata.
- **SelectFallback:** Drops unsupported fields (`ProjectCategory`, `StorageWarehouseId`, etc.).
- **GetQuantity:** `"Quantity"` → `"Qty"`.
- **GetUnitPrice:** Same normalization logic as Item policy.

#### **HourJournalFetchPolicy** (`src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/FscmJournalPolicies/HourJournalFetchPolicy.cs`)

- **Purpose:** Policy for **Hour** journals (`JournalType.Hour`) .
- **EntitySet:** `JournalTrans`
- **Select Fields:** `Hours`,`SalesPrice`,`LineProperty`,`DimensionDisplayValue`,`ProjectDate`
- **GetQuantity:** `"Hours"` → `"Qty"` → `"Quantity"`.
- **GetUnitPrice:** `"SalesPrice"` → `"ProjectSalesPrice"`.

#### **FscmJournalFetchPolicyResolver** (`src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/FscmJournalPolicies/FscmJournalFetchPolicyResolver.cs`)

- **Purpose:** Maps `JournalType` to its `IFscmJournalFetchPolicy` implementation, preventing duplicates .
- **Methods:**- Constructor(IEnumerable<IFscmJournalFetchPolicy>)
- `IFscmJournalFetchPolicy Resolve(JournalType journalType)`

#### **FscmJournalFetchHttpClient** (`src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/FscmJournalFetchHttpClient.cs`)

- **Purpose:** Fetches and parses FSCM journal lines by batched WorkOrder GUIDs.
- **Key Behaviors:**- **FetchByWorkOrdersAsync:** Batches IDs, builds OData filter URLs, handles HTTP headers/correlation.
- **Error Handling:** Retries on transient (>=500, 429), fails fast on auth, warns and falls back on missing‐field errors via `SelectFallback`.
- **Parsing:** `ParseODataValueArrayToJournalLines` transforms JSON array into `FscmJournalLine` instances, leveraging policy mapping (`GetQuantity`, `GetUnitPrice`) and building payload snapshots for reversal planning.
- **Dependencies:** `HttpClient`, `FscmOptions`, `FscmJournalFetchPolicyResolver`, `ILogger`.

## Key Classes Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| IFscmJournalFetchPolicy | Adapters/Fscm/Clients/FscmJournalPolicies/IFscmJournalFetchPolicy.cs | Defines metadata & mapping contract per journal type |
| FscmJournalFetchPolicyBase | Adapters/Fscm/Clients/FscmJournalPolicies/FscmJournalFetchPolicyBase.cs | Abstract base with common JSON parsing & fallback support |
| ItemJournalFetchPolicy | Adapters/Fscm/Clients/FscmJournalPolicies/ItemJournalFetchPolicy.cs | Implements mapping for Item journal entity set |
| ExpenseJournalFetchPolicy | Adapters/Fscm/Clients/FscmJournalPolicies/ExpenseJournalFetchPolicy.cs | Implements mapping for Expense journal entity set |
| HourJournalFetchPolicy | Adapters/Fscm/Clients/FscmJournalPolicies/HourJournalFetchPolicy.cs | Implements mapping for Hour journal entity set |
| FscmJournalFetchPolicyResolver | Adapters/Fscm/Clients/FscmJournalPolicies/FscmJournalFetchPolicyResolver.cs | Resolves policies by `JournalType` |
| FscmJournalFetchHttpClient | Adapters/Fscm/Clients/FscmJournalFetchHttpClient.cs | HTTP client that fetches & normalizes FSCM journal lines |


## Dependencies

- **System.Text.Json**: JSON parsing and `JsonElement` manipulation.
- **HttpClient**: Performing OData HTTP requests.
- **Microsoft.Extensions.Logging**: Structured logging of fetch lifecycle.
- **Rpc.AIS.Accrual.Orchestrator.Core.Domain**: `JournalType`, `FscmJournalLine`.
- **Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options**: `FscmOptions` for base URLs and chunk sizes.

---

✨ This modular policy-based approach simplifies adding new journal types and handling environment variances without altering the core HTTP fetch logic.