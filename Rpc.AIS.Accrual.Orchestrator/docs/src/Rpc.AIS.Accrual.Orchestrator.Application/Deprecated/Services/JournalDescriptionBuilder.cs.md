# Journal Description Builder Feature Documentation

## Overview 📖

The **JournalDescriptionBuilder** provides a consistent way to construct the `JournalDescription` text used in FSCM payloads. It combines three inputs—**Job ID**, **Sub-Project ID**, and an **Action** suffix—into a human-readable string. This description is stamped at the **journal section** level to help tracing, logging, and diagnostics throughout the accrual orchestrator pipeline.

Located in the **Deprecated** services folder, this builder implements the core contract **IJournalDescriptionBuilder** and is registered in dependency injection for any component needing to generate or normalize journal descriptions .

## Component Structure 🔧

### JournalDescriptionBuilder

**Location:** `src/Rpc.AIS.Accrual.Orchestrator.Application/Deprecated/Services/JournalDescriptionBuilder.cs`

Implements: **IJournalDescriptionBuilder**

- **Purpose:**- Build a single description string from three parts.
- Guard against `null` inputs by converting them to empty strings.

- **Key Method:**

| Method | Signature | Description |
| --- | --- | --- |
| Build | `string Build(string jobId, string subProjectId, string action)` | Returns `"{jobId} - {subProjectId} - {action}"`, with null-coalescing to empty |


- **Implementation:**

```csharp
  public sealed class JournalDescriptionBuilder : IJournalDescriptionBuilder
  {
      public string Build(string jobId, string subProjectId, string action)
      {
          jobId       ??= string.Empty;
          subProjectId??= string.Empty;
          action      ??= string.Empty;
          return $"{jobId} - {subProjectId} - {action}";
      }
  }
```

### IJournalDescriptionBuilder

**Location:** `src/Rpc.AIS.Accrual.Orchestrator.Application/Ports/Common/Abstractions/IJournalDescriptionBuilder.cs`

- **Purpose:** Defines the contract for any service that generates a journal description string.
- **Method:**

| Method | Signature | Description |
| --- | --- | --- |
| Build | `string Build(string jobId, string subProjectId, string action)` | Combines inputs into a single description. |


## Registration & Usage ⚙️

- **Dependency Injection:**

Registered as a singleton so that any component can depend on the interface rather than the concrete type:

```csharp
  services.AddSingleton<IJournalDescriptionBuilder, JournalDescriptionBuilder>();
```

- **Typical Consumers:**- Payload-builders that need to stamp or override the `JournalDescription` header.
- Logging or diagnostics components that include this description in trace events.

## Dependencies 🔗

- **Namespace:**- `Rpc.AIS.Accrual.Orchestrator.Core.Abstractions` (for the interface)
- `Rpc.AIS.Accrual.Orchestrator.Core.Services` (for the implementation)

- **External:** None beyond the shared orchestration abstractions.

## Testing Considerations ✔️

- **Null Safety:**

Ensure that passing `null` for any parameter yields empty segments rather than throwing.

- **Formatting:**

Verify that the output always has two separators (`" - "`) even when parts are empty.

- **Examples:**

| Inputs | Output |
| --- | --- |
| (`"JOB1"`, `"SP42"`, `"Post"`) | `"JOB1 - SP42 - Post"` |
| (null, `"SP42"`, null) | `" - SP42 - "` |
| (null, null, null) | `" -  - "` |


---