# AIS Diagnostics Options Feature Documentation

## Overview

The **AIS Diagnostics Options** feature centralizes configuration for diagnostic logging within the Accrual Orchestrator. It defines an abstraction (`IAisDiagnosticsOptions`) that drives runtime behaviors such as payload logging, snippet lengths, and chunk sizes. Concrete implementations bind these values from configuration and enable services (e.g., posting clients, telemetry) to respect diagnostic settings consistently across the application.

This feature:

- Allows toggling of raw payload logging and multi-work-order payload logging.
- Controls how large payload snippets and chunks are when emitted to logs.
- Supports testability via a fake implementation supplying deterministic defaults.

## Architecture Overview

```mermaid
flowchart TB
    subgraph Configuration
        Cfg[AisLogging:* Configuration Section]
        AO[AisDiagnosticsOptions POCO]
    end
    subgraph Adapter
        ADA[AisDiagnosticsOptionsAdapter]
    end
    subgraph CoreAbstractions
        DIA[IAisDiagnosticsOptions]
    end
    subgraph Services
        PSC[FscmJournalPoster | ...]
        TEL[Telemetry / Logging]
    end

    Cfg --> AO
    AO --> ADA
    ADA --> DIA
    DIA --> PSC
    DIA --> TEL
```

## Component Structure

### Core Abstractions

#### **IAisDiagnosticsOptions** (`src/Rpc.AIS.Accrual.Orchestrator.Application/Ports/Common/Abstractions/IAisDiagnosticsOptions.cs`)

- Defines the contract for diagnostic logging options used by core services.
- Implementations must expose read-only properties for toggling logging and sizing rules.

| Property | Type | Description |
| --- | --- | --- |
| LogPayloadBodies | bool | When **true**, entire JSON payloads are logged. |
| LogMultiWoPayloadBody | bool | When **true**, multi-work-order payload bodies are logged. |
| IncludeDeltaReasonKey | bool | When **true**, includes the delta reason key in log entries. |
| PayloadSnippetChars | int | Maximum number of characters to include in a payload snippet. |
| PayloadChunkChars | int | Maximum number of characters per log chunk when splitting large payloads. |


```csharp
namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
/// <summary>Defines i ais diagnostics options behavior.</summary>
public interface IAisDiagnosticsOptions
{
    bool LogPayloadBodies { get; }
    bool LogMultiWoPayloadBody { get; }
    bool IncludeDeltaReasonKey { get; }
    int PayloadSnippetChars { get; }
    int PayloadChunkChars { get; }
}
```

### Configuration Classes

#### **AisDiagnosticsOptions** (`src/Rpc.AIS.Accrual.Orchestrator.Application/Options/AisDiagnosticsOptions.cs`)

- **POCO** bound from the `AisLogging` section in configuration.
- Supplies default values (e.g., `PayloadSnippetChars = 4000`).

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| LogPayloadBodies | bool | — | Toggle full payload logging. |
| LogMultiWoPayloadBody | bool | — | Toggle multi-WO payload logging. |
| IncludeDeltaReasonKey | bool | — | Include delta reason in logs. |
| PayloadChunkChars | int | — | Max chars per log chunk. |
| PayloadSnippetChars | int | 4000 | Max chars for payload snippet. |


```csharp
public sealed class AisDiagnosticsOptions
{
    public bool LogPayloadBodies { get; init; }
    public bool LogMultiWoPayloadBody { get; init; }
    public bool IncludeDeltaReasonKey { get; init; }
    public int PayloadChunkChars { get; init; }
    public int PayloadSnippetChars { get; init; } = 4000;
}
```

#### **AisDiagnosticsOptionsAdapter** (`src/Rpc.AIS.Accrual.Orchestrator.Application/Options/AisDiagnosticsOptionsAdapter.cs`)

- Wraps the POCO and implements the interface for DI.
- Ensures null safeguards when injected.

```csharp
public sealed class AisDiagnosticsOptionsAdapter : IAisDiagnosticsOptions
{
    private readonly AisDiagnosticsOptions _o;
    public AisDiagnosticsOptionsAdapter(AisDiagnosticsOptions o)
        => _o = o ?? throw new ArgumentNullException(nameof(o));

    public bool LogPayloadBodies => _o.LogPayloadBodies;
    public bool LogMultiWoPayloadBody => _o.LogMultiWoPayloadBody;
    public bool IncludeDeltaReasonKey => _o.IncludeDeltaReasonKey;
    public int PayloadSnippetChars => _o.PayloadSnippetChars;
    public int PayloadChunkChars => _o.PayloadChunkChars;
}
```

### Testing Doubles

#### **FakeAisDiagnosticsOptions** (`tests/Rpc.AIS.Accrual.Orchestrator.Tests/TestDoubles/FakeAisDiagnosticsOptions.cs`)

- Provides a simple test implementation of `IAisDiagnosticsOptions`.
- Supplies deterministic defaults for unit and integration tests.

```csharp
public sealed class FakeAisDiagnosticsOptions : IAisDiagnosticsOptions
{
    public bool LogPayloadBodies { get; init; } = false;
    public bool LogMultiWoPayloadBody { get; init; } = false;
    public bool IncludeDeltaReasonKey { get; init; } = false;
    public int PayloadSnippetChars { get; init; } = 512;
    public int PayloadChunkChars { get; init; } = 4096;
}
```

## Usage Flow

1. **Startup** binds the `AisLogging` section to `AisDiagnosticsOptions` and registers the adapter with DI.
2. **Services** such as `FscmJournalPoster` and `IPostOutcomeProcessor` inject `IAisDiagnosticsOptions` to determine logging behavior **before** emitting payloads.
3. **Tests** swap in `FakeAisDiagnosticsOptions` to assert logic under controlled diagnostic settings.

## Key Classes Reference

| Class | Path | Responsibility |
| --- | --- | --- |
| IAisDiagnosticsOptions | src/.../Abstractions/IAisDiagnosticsOptions.cs | Defines diagnostic logging options contract. |
| AisDiagnosticsOptions | src/.../Options/AisDiagnosticsOptions.cs | POCO for binding `AisLogging` configuration. |
| AisDiagnosticsOptionsAdapter | src/.../Options/AisDiagnosticsOptionsAdapter.cs | Adapts POCO to the interface for DI. |
| FakeAisDiagnosticsOptions | tests/.../FakeAisDiagnosticsOptions.cs | Test double implementing the diagnostics interface. |


## Testing Considerations

- **FakeAisDiagnosticsOptions** defaults allow tests to disable logging overhead.
- Services consuming `IAisDiagnosticsOptions` should be exercised under both `true` and `false` toggles to verify conditional logging paths.