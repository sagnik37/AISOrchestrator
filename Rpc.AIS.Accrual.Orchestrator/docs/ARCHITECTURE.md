# AIS Accrual Orchestrator — Architecture

## Layering

**Functions**
- HTTP endpoints, triggers, Durable orchestration wiring
- DI composition root
- No business rules

**Application**
- Use-cases, policies, deterministic computation, validation rules
- Defines *ports* (interfaces) that Infrastructure implements

**Domain**
- Pure domain types (entities/value objects/enums) and invariants

**Infrastructure**
- Adapters for external systems: FSCM, Dataverse/FSA, email, storage
- HTTP clients, resilience, telemetry/logging implementations

## Folder conventions (current)

- `src/Rpc.AIS.Accrual.Orchestrator.Domain/Domain/*`
- `src/Rpc.AIS.Accrual.Orchestrator.Application/Features/*`
- `src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/*`
- `src/Rpc.AIS.Accrual.Orchestrator.Functions/Endpoints/*` and `Durable/*`

## SOLID hygiene rules

- Keep feature ownership clear (no "misc" services).
- Prefer pure, deterministic services in Application; IO behind ports.
- Composition only in Functions.
