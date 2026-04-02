# AIS Work Order Integration  
### Dynamics 365 Field Service → D365 Finance & Supply Chain  

**Solution:** `Rpc.AIS.Accrual.Orchestrator`  
**Runtime:** .NET 8 Azure Functions (Isolated Worker)  
**Architecture Style:** Clean Architecture + Ports & Adapters + Durable Orchestration  

---

# 1. Purpose of the Solution

AIS (Accrual Integration Service) synchronizes Work Orders and Work Order Lines from:

- Dynamics 365 Field Service (Dataverse)

Into:

- D365 Finance & Supply Chain Management (FSCM)

The system performs:

- Journal validation  
- Journal creation  
- Journal posting  
- Sub-project provisioning  
- Invoice attribute updates  
- Project status updates  
- Fiscal period adjustments  
- Delta computation between FSA and FSCM  

---

# 2. High-Level Runtime Architecture
Azure Function App
│
├── HTTP Triggers (AdHoc / Cancel / CustomerChange)
├── Timer Trigger (Scheduled Accrual)
│
├── Durable Orchestrators
│ ├── Fetch FSA data
│ ├── Fetch FSCM history
│ ├── Compute delta
│ ├── Validate journals
│ ├── Create journals
│ ├── Post journals
│ ├── Update invoice attributes
│ └── Update project status
│
├── Application Layer (UseCases)
├── Core Layer (Delta & Domain Rules)
├── Infrastructure Layer (Dataverse + FSCM clients)
└── Observability (App Insights + Structured Logging)


---

# 3. Solution Structure (Project Breakdown)

---

## 3.1 Rpc.AIS.Accrual.Orchestrator.Functions

### Purpose:
Entry point of the application. Hosts all Azure Function triggers and orchestrators.

### Contains:
- Timer trigger  
- HTTP triggers (AdHoc, Cancel, Customer Change)  
- Durable orchestrators  
- Activity functions  
- Composition root (DI registration)  

### Key Responsibilities:
- Accept requests  
- Create RunId / CorrelationId  
- Start durable orchestration  
- Return HTTP responses  
- Maintain orchestration state  

---

## 3.2 Rpc.AIS.Accrual.Orchestrator.Application

### Purpose:
Implements business workflows (UseCases).

### Contains:
- Posting workflow orchestration logic  
- Delta payload builder  
- Sub-project workflow  
- Validation workflow  
- Finalization workflow  

### Responsibilities:
- Coordinate multiple services  
- Convert domain models to integration payloads  
- Enforce orchestration order  
- Apply business rules  

---

## 3.3 Rpc.AIS.Accrual.Orchestrator.Core

### Purpose:
Contains domain logic and business engines.

### Contains:
- DeltaCalculationEngine  
- FiscalPeriodAdjustmentEngine  
- Journal section builders  
- Domain models (DeltaPlannedLine, FsaDeltaSnapshot, etc.)  

### Responsibilities:
- Compute line-level deltas  
- Determine create/update/reversal  
- Apply period logic  
- Enforce financial correctness  

---

## 3.4 Rpc.AIS.Accrual.Orchestrator.Infrastructure

### Purpose:
External system adapters.

### Contains:
- Dataverse API client  
- FSCM OData clients  
- FSCM Custom Service clients  
- Retry policies  
- Caching logic  
- Auth token providers  

### Responsibilities:
- Call Dataverse  
- Call FSCM OData  
- Call FSCM custom services  
- Handle retries and resilience  
- Map HTTP responses  

---

## 3.5 Rpc.AIS.Accrual.Orchestrator.Domain 

### Purpose:
Pure models and value objects.

### Contains:
- Domain DTOs  
- Result models  
- Validation results  
- Posting result structures  

---

# 4. Runtime Execution – Scenario by Scenario

---

## 4.1 Timer Trigger (Scheduled Accrual)

### Trigger
`TimerTrigger("AccrualSchedule")`

### Flow

- Timer fires  
- Creates RunId  
- Starts Durable Orchestrator  
- Fetch open Work Orders from Dataverse  

For each Work Order:

- Fetch FSCM history  
- Compute delta  
- Validate  
- Create journals  
- Post journals  
- Update invoice attributes  
- Update project status  

### Classes Involved

- AccrualTimerFunction  
- AccrualOrchestrator  
- GetFsaDeltaPayloadActivity  
- BuildDeltaPayloadFromFscmHistoryActivity  
- WoPayloadPostingActivity  
- FinalizeWoPayloadActivity  

---

## 4.2 AdHoc Single

### Endpoint
`POST /api/adhoc/batch/single`

### Purpose
Process one Work Order manually.

### Flow
- Accept WorkOrderGuid  
- Start orchestration for single WO  
- Same pipeline as Timer, but scoped  

### Classes
- AdHocBatchSingleFunction  
- AdHocBatchOrchestrator  

---

## 4.3 AdHoc Bulk

### Endpoint
`POST /api/adhoc/batch/bulk`

### Purpose
Process multiple Work Orders at once.

### Differences from Single
- WOList passed in payload  
- Loops within orchestration  

---

## 4.4 Customer Change Flow

### Endpoint
`POST /api/customer/change`

### Purpose
Handle:

- Sub-project changes  
- Reverse old accruals  
- Create new sub-project  
- Repost journals  

### Flow

- Fetch existing FSCM journals  
- Reverse if required  
- Create new sub-project  
- Recreate journals  

### Core Classes

- CustomerChangeOrchestrator  
- SubProjectCreationClient  
- DeltaJournalSectionBuilder  

---

## 4.5 Cancel Job

### Endpoint
`POST /api/job/cancel`

### Purpose
Cancel Work Order in FSCM.

### Logic

- If payload empty → update status only  
- Else reverse journals  
- Update project status  

### Classes

- CancelJobFunction  
- CancelJobOrchestrator  

---

## 4.6 Posting Workflow (Core Engine)

### Pipeline

- Validate (FSCM remote validation)  
- Create journals  
- Post journals  
- Update invoice attributes  
- Update project status  

### Classes

- FscmWoPayloadValidationClient  
- FscmJournalOperationClient  
- FscmJournalPostingClient  
- FscmInvoiceAttributesClient  
- FscmSubProjectStatusClient  

---

## 4.7 Invoice Attributes Update

### Executed:
- After successful posting  
- Or independently (if configured)  

### Data Source

From Work Order header:

- Well name  
- Well number  
- Area  
- Rig  
- Latitude/Longitude  
- PO number  

---

# 5. Delta Engine (Core Logic)

## DeltaCalculationEngine

### Determines:
- New lines  
- Changed lines  
- Deleted lines  
- Quantity change  
- Price change  
- Discount change  
- Period change  

### Produces:
FsaDeltaSnapshot
├── ItemLines
├── ExpenseLines
└── HourLines


---

# 6. Fiscal Period Adjustment

If OperationDate is in closed period:
TransactionDate = Next open period start date
OperationDate = Original FS date


### Class:
FiscalPeriodAdjustmentEngine  

### Uses:
`Fscm:Periods:FiscalCalendarIdOverride`

---

# 7. How Projects Interact at Runtime

Functions → Application → Core → Infrastructure

- Functions layer: orchestration  
- Application layer: workflow logic  
- Core layer: financial computation  
- Infrastructure layer: HTTP + Auth  

Dependency direction strictly inward (SOLID compliant).

---

# 8. SOLID Principles Applied

## SRP
Each UseCase performs one workflow.

## OCP
Delta rules extendable via new policy classes.

## LSP
Interfaces define behavior contracts.

## ISP
Clients implement narrow interfaces:

- IFscmJournalPoster  
- IFscmPayloadValidator  
- IFsaLineFetcher  

## DIP
Functions depend only on abstractions.

---

# 9. Logging & Observability

- RunId + CorrelationId per execution  
- Structured logging with scopes  
- Payload hash logging  
- Delta reason keys  
- App Insights dependency tracking  

---

# 10. Error Handling Strategy

| Error Type | Behavior |
|------------|----------|
| FSCM 500 validation | Stop workflow |
| FSCM 500 create | Do not retry blindly |
| Dataverse 429 | Retry with backoff |
| Timeout | Retry if idempotent |

---

# 11. Extending the System (Adding New Feature)

### Example: Add "Pre-Posting Audit Service"

### Steps:

1. Add Interface in Core:  
   `IPrePostingAuditService`

2. Implement in Infrastructure  

3. Inject into Application workflow  

4. Call before validation  

5. Log results  

6. Update TDD  

No changes required in:

- Orchestrator structure  
- Core delta engine  

This demonstrates OCP compliance.

---

# 12. Security Model

- AAD App Registrations for Dataverse & FSCM  
- Secrets stored in Key Vault  
- HTTP policies protect against transient failures  
- Payload logging disabled in production  

---

# 13. Key Configuration Areas

- Dataverse filters  
- Fiscal calendar override  
- Retry policies  
- Cache TTL  
- Email distribution lists  

---

# 14. Summary

AIS is:

- A resilient, enterprise-grade integration engine  
- Designed with clean architecture  
- Durable orchestration-based  
- Financially accurate via delta computation  
- Extensible without breaking existing flows  
- Fully observable  
