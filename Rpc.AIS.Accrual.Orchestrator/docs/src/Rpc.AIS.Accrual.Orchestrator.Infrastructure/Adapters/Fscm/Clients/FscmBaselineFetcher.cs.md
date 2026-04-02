# FSCM Baseline Fetcher Feature Documentation

## Overview

The **FSCM Baseline Fetcher** is an infrastructure component responsible for retrieving a snapshot of existing journal lines (baseline data) from the FSCM system via OData. It iterates over configured entity sets (e.g., Item, Expense, Hour), applies optional filters, and computes a SHA-256 hash for each record. These **baseline records** serve as the basis for subsequent delta calculations, enabling the orchestrator to detect added, changed, or removed lines when comparing against FSA-provided data. By centralizing baseline retrieval, the fetcher ensures consistent, repeatable snapshots across runs and environments .

## Architecture Overview

```mermaid
flowchart TB
    subgraph Orchestrator [FsaDeltaPayloadUseCase]
        A[FsaDeltaPayloadUseCase]
    end
    subgraph Infrastructure [Data Access Layer]
        B[FscmBaselineFetcher]
    end
    subgraph FSCM [FSCM OData Service]
        C[FSCM OData Endpoint]
    end

    A -->|FetchBaselineAsync| B
    B -->|HTTP GET {entitySet}| C
```

- **FsaDeltaPayloadUseCase** invokes `FetchBaselineAsync` on `FscmBaselineFetcher`.
- **FscmBaselineFetcher** issues HTTP GET requests to each configured OData entity set.
- **FSCM OData Service** returns JSON arrays of journal records.

## Component Structure

### 1. Data Access Layer

#### **FscmBaselineFetcher** (`src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/FscmBaselineFetcher.cs`)

- **Purpose:** Implements `IFscmBaselineFetcher` to retrieve and hash baseline journal lines from FSCM .
- **Dependencies:**- `HttpClient _http`
- `IOptions<FscmOptions> _opt`
- `ILogger<FscmBaselineFetcher> _log`

##### Constructor

```csharp
public FscmBaselineFetcher(
    HttpClient http,
    IOptions<FscmOptions> opt,
    ILogger<FscmBaselineFetcher> log)
```

- Validates non-null dependencies.
- `HttpClient` is preconfigured with FSCM base URL and authentication handler.

##### Public Methods

| Method | Description | Returns |
| --- | --- | --- |
| `FetchBaselineAsync(CancellationToken ct)` |


- Checks `BaselineEnabled`.
- Validates `BaselineODataBaseUrl`.
- Iterates `BaselineEntitySets`.
- Builds URL with optional `$filter`.
- Logs request and response sizes.
- Parses JSON `value` array.
- Extracts `WorkOrderId` as string.
- Computes SHA-256 hash of raw JSON.
- Populates `FscmBaselineRecord` list. | `Task<IReadOnlyList<FscmBaselineRecord>>`  |

##### Private Helpers

- `static string JournalTypeFromSet(string entitySet)`

Infers `"Hour"`, `"Expense"`, or `"Item"` based on entity set name .

- `static string Sha256Hex(string s)`

Computes lowercase hex SHA-256 of input string .

### 2. Data Models

#### **FscmBaselineRecord** (`Rpc.AIS.Accrual.Orchestrator.Core.Domain`)

Carries baseline record metadata used in delta comparison.

| Property | Type | Description |
| --- | --- | --- |
| `WorkOrderNumber` | string | Work order identifier extracted from the JSON row. |
| `JournalType` | string | Inferred type: `"Hour"`, `"Expense"`, or `"Item"`. |
| `LineKey` | string | Unique key combining entity set and first 12 hex chars of the hash. |
| `Hash` | string | Full lowercase SHA-256 hex of the JSON row. |


## API Integration

### Fetch FSCM Baseline Records (GET)

```api
{
    "title": "Fetch FSCM Baseline Records",
    "description": "Retrieves baseline journal records from FSCM for a specified entity set.",
    "method": "GET",
    "baseUrl": "<BaselineODataBaseUrl>",
    "endpoint": "/{entitySet}",
    "headers": [
        {
            "key": "Accept",
            "value": "application/json",
            "required": true
        }
    ],
    "queryParams": [
        {
            "key": "$filter",
            "value": "BaselineInProgressFilter (optional)",
            "required": false
        }
    ],
    "pathParams": [
        {
            "key": "entitySet",
            "value": "Name of the OData entity set",
            "required": true
        }
    ],
    "bodyType": "none",
    "requestBody": "",
    "formData": [],
    "rawBody": "",
    "responses": {
        "200": {
            "description": "Success",
            "body": "{\n  \"value\": [ /* array of JSON objects */ ]\n}"
        },
        "4xx": {
            "description": "Client error",
            "body": "{ \"error\": { \"message\": \"...\" } }"
        }
    }
}
```

## Key Classes Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| **FscmBaselineFetcher** | `Infrastructure/Adapters/Fscm/Clients/FscmBaselineFetcher.cs` | Implements `IFscmBaselineFetcher` to fetch and hash baseline journal data. |
| **FscmBaselineRecord** | `Core/Domain/FscmBaselineRecord.cs` | DTO for individual baseline entries. |


## Dependencies

- **HttpClient**: Communicates with FSCM OData endpoints.
- **FscmOptions** (`Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options`):- `BaselineEnabled` (bool)
- `BaselineODataBaseUrl` (string)
- `BaselineEntitySets` (IEnumerable<string>)
- `BaselineInProgressFilter` (string)
- **Logging**: `ILogger<FscmBaselineFetcher>` logs fetch lifecycle and metrics.