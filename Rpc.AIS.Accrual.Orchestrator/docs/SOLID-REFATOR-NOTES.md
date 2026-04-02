# SOLID Refactor Notes

## Applied in this package

### 1. Dependency injection composition root split by responsibility
The former single `ServiceCollectionExtensions.cs` file carried option binding, domain services, function-layer registrations, HTTP client registrations, retry policy wiring, and cache setup in one place.

It has been split into:

- `ServiceCollectionExtensions.cs` – observability only
- `ServiceCollectionExtensions.Core.cs` – options, domain services, cross-cutting services, function-layer registrations, enrichment pipeline
- `ServiceCollectionExtensions.Infrastructure.cs` – typed HTTP clients, retry policies, cache decorators, FSCM/Dataverse registrations

This improves:

- **SRP**: each file now has one registration concern
- **OCP**: adding a new FSCM typed client can use the shared generic helper
- **DIP**: interface-to-implementation mappings are more visible and grouped logically
- **Maintainability**: lower cognitive load when changing one slice of the host

### 2. Typed FSCM client registration abstraction
Repeated blocks for FSCM typed clients were consolidated behind `RegisterTypedFscmClient<TImplementation, TContract>()`.

This reduces duplication and makes future client additions safer and more consistent.

## Recommended next refactor phases

### Phase 2 – Delta payload pipeline decomposition
Priority hotspots:

- `DeltaJournalSectionBuilder`
- `DeltaPayloadBuilder`
- `WoLocalValidator`
- `FsaDeltaPayloadJsonInjector`

Recommended extraction pattern:

- line context reader
- delta decision coordinator
- reversal/recreate projector
- line payload sanitizer
- dimension display value stamper
- date stamping policy

### Phase 3 – Invoice attribute workflow decomposition
Priority hotspot:

- `InvoiceAttributeSyncRunner`

Recommended collaborators:

- work-order payload reader
- FSCM snapshot cache service
- FS attribute extractor
- derived attribute calculator
- FSCM attribute delta planner
- payload injector

### Phase 4 – Durable/orchestration simplification
Priority hotspots:

- `CustomerChangeOrchestrator`
- `JobOperationsUseCaseBase`
- `PostJobUseCase`
- `CancelJobUseCase`

Recommended approach:

- introduce narrow step services
- isolate transport concerns from business rules
- move branching rules into strategy services
- keep orchestrators as coordinators only

## Constraints

This pass focused on **safe structural refactoring** with minimal behavioral change risk. A stricter full-SOLID pass across the entire repository would require a second wave with build-and-test validation in a local .NET SDK environment.
