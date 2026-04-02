# Log Scope Keys Feature Documentation

## Overview

The **LogScopeKeys** class centralizes the string keys used for structured logging scopes throughout the Orchestrator infrastructure. By defining these constants in one place, the application ensures consistency and reduces the risk of typos when creating log scopes. These keys serve as dictionary entries when calling `ILogger.BeginScope`, allowing downstream logging sinks (e.g., Application Insights) to capture rich, contextual information for each function execution.

In the broader application, these keys tie together the `LogScopes` helper and the `LogScopeContext` record. Together, they standardize how runtime metadata—such as run IDs, correlation IDs, and domain-specific identifiers—is propagated into every log entry.

---

## LogScopeKeys Class (`src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Logging/LogScopeKeys.cs`)

- **Type**: `public static class`
- **Purpose**: Define constant field names for logging scopes.
- **Location**: `Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging`

```csharp
namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

public static class LogScopeKeys
{
    public const string RunId          = "RunId";
    public const string CorrelationId  = "CorrelationId";
    public const string SourceSystem   = "SourceSystem";
    public const string Function       = "Function";
    public const string Activity       = "Activity";
    public const string Operation      = "Operation";
    public const string WorkOrderGuid  = "WorkOrderGuid";
    public const string WorkOrderId    = "WorkOrderId";
    public const string SubProjectId   = "SubProjectId";
    public const string Company        = "Company";
    public const string JournalType    = "JournalType";
}
```

---

## Constants Table 🗝️

| Constant | Value | Description |
| --- | --- | --- |
| RunId | `"RunId"` | Unique identifier for a single function or workflow execution. |
| CorrelationId | `"CorrelationId"` | Identifier to correlate operations across distributed services. |
| SourceSystem | `"SourceSystem"` | Name of the originating system or component. |
| Function | `"Function"` | Name of the Azure Function or entry-point being executed. |
| Activity | `"Activity"` | Specific activity or step within a durable orchestration. |
| Operation | `"Operation"` | Logical operation name (e.g., PostJob, CustomerChange). |
| WorkOrderGuid | `"WorkOrderGuid"` | GUID of the work order associated with the current operation. |
| WorkOrderId | `"WorkOrderId"` | Business-facing identifier of the work order. |
| SubProjectId | `"SubProjectId"` | Identifier for a sub-project within a larger work order. |
| Company | `"Company"` | Company code or tenant identifier in multi-tenant scenarios. |
| JournalType | `"JournalType"` | Domain-specific enumeration indicating the journal entry type. |


---

## Usage

These constants are referenced when building the logging scope dictionary in helper methods such as `LogScopes.BeginFunctionScope`. Only non-null values are included, keeping the scope lean.

```csharp
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

// ...

var scopeDict = new Dictionary<string, object?>(capacity: 12)
{
    [LogScopeKeys.RunId]         = ctx.RunId,
    [LogScopeKeys.CorrelationId] = ctx.CorrelationId,
    [LogScopeKeys.SourceSystem]  = ctx.SourceSystem,
    [LogScopeKeys.Function]      = ctx.Function,
    [LogScopeKeys.Operation]     = ctx.Operation,
    // … other keys …
};

RemoveNulls(scopeDict);
return logger.BeginScope(scopeDict);
```

---

## Integration Points

- **LogScopes**: Builds structured scopes using these keys.
- **LogScopeContext**: Supplies typed properties whose values map to each key.
- **ILogger**: Receives the scope dictionary for enriched, contextual logging.

---

## Design Considerations

- **Centralization**: Prevents discrepancies by avoiding scattered string literals.
- **Expandability**: New scope keys can be added easily as application needs evolve.
- **Performance**: Using constants avoids repeated allocations of identical strings.

---

## See Also

- `LogScopes` helper class (`src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Logging/LogScopes.cs`)
- `LogScopeContext` record (`src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Logging/LogScopeContext.cs`)

---

> “Consistency in logging keys leads to reliable telemetry and simpler diagnostics.”