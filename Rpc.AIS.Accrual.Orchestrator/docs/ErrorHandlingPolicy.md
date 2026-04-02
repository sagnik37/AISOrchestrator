# AIS Error Handling Policy

This policy defines a consistent error-handling approach across AIS (Accrual Integration Service).  
The intent is to make behavior predictable for a financial integration while preserving diagnostics.

## Definitions

- **Expected / business failure**: validation failures, missing reference data, rejected postings, domain constraints, etc.
- **Unexpected / infrastructure failure**: networking, auth, timeouts, serialization defects, transient upstream faults, misconfiguration.

## Policy

### 1) Use Result objects for expected business failures
For failures that can reasonably occur during normal operation and should be reported to callers in a structured way:

- Return a dedicated **Result** type (e.g., `SubProjectCreateResult`, `PostResult`, validation result models).
- Include:
  - `IsSuccess`/status
  - failure codes/reasons
  - any safe-to-log upstream diagnostics
  - correlation identifiers (`RunId`, `CorrelationId`) where available.

### 2) Use exceptions for unexpected / infrastructure failures
For defects or failures that indicate the system cannot safely proceed:

- Throw exceptions (or let them bubble) and rely on centralized logging/telemetry.
- Wrap only when adding actionable context (endpoint name, operation, elapsed time, correlation IDs).

### 3) No silent catch blocks
Never swallow exceptions without:
- logging (at least `Warning`), and
- a clear fallback decision (returning input unchanged, returning a failure Result, etc.)

If a fallback is required to preserve behavior, it must be explicit and observable.

## Guidance
- Prefer **typed Result** for FSCM validation/create/post errors where the caller needs the full upstream message.
- Prefer exceptions for configuration errors, unexpected nulls, serializer failures, and coding defects.
