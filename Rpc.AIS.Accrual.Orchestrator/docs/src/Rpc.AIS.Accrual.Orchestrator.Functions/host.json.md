# Azure Functions Host Configuration (host.json)

## Overview

The **host.json** file defines global settings for the Azure Functions host in the

Rpc.AIS.Accrual.Orchestrator.Functions project. It controls runtime behavior,

notably the Durable Functions concurrency limits, and specifies which extension bundle

the Functions runtime should load. This ensures consistent performance and maintains

compatibility with required extensions.

## Configuration Elements

Below are the top-level sections in **host.json**:

- **version**: Specifies the major version of the Functions host schema (here, 2.0).
- **extensions.durableTask**: Tuning parameters for Durable Functions concurrency.
- **extensionBundle**: Identifies and versions the bundle of extensions to load.

## Durable Task Settings ⚙️

These settings govern how many orchestrator and activity functions can run concurrently.

| Property | Description | Value |
| --- | --- | --- |
| **maxConcurrentActivityFunctions** | Maximum number of activity functions executing in parallel. | 32 |
| **maxConcurrentOrchestratorFunctions** | Maximum number of orchestrator functions executing in parallel. | 16 |


- By default, Azure Functions scales activities and orchestrations horizontally.
- These limits help prevent resource exhaustion when scheduling many Durable Function instances.

## Extension Bundle Settings 📦

The **extensionBundle** section tells the Functions host which pre-packaged extensions to load.

| Property | Description | Value |
| --- | --- | --- |
| **id** | NuGet identifier for the extension bundle. | Microsoft.Azure.Functions.ExtensionBundle |
| **version** | Semantic version range for bundle updates. | \[4.*, 5.0.0) |


- The version range `[4.*, 5.0.0)` ensures any 4.x bundle is acceptable but prevents auto-upgrading to a 5.0 bundle.
- This bundle includes DurableTask, HTTP binding, and other common extensions.

## Example Configuration

```json
{
  "version": "2.0",
  "extensions": {
    "durableTask": {
      "maxConcurrentActivityFunctions": 32,
      "maxConcurrentOrchestratorFunctions": 16
    }
  },
  "extensionBundle": {
    "id": "Microsoft.Azure.Functions.ExtensionBundle",
    "version": "[4.*, 5.0.0)"
  }
}
```

## Integration Points 🔗

- **Durable Functions**

The `durableTask` settings directly affect all orchestrators and activities in

the `Rpc.AIS.Accrual.Orchestrator.Functions` project, such as `AccrualOrchestratorFunctions`

and `JobOperationsOrchestration`.

- **Extension Bundle Loading**

By specifying an extension bundle, the host automatically imports bindings and

middleware required by the code—no manual NuGet references for each extension.

## Dependencies

- Azure Functions runtime (v2+).
- DurableTask extension (via extension bundle).
- Other Azure Function extensions (HTTP, Timers, etc.) are provisioned by the bundle.

## Testing Considerations 🧪

- Integration tests against Durable Functions should respect the concurrency limits

defined here; tests that spin up many orchestrations may hit the `maxConcurrent*` caps.

- Local debugging honors this configuration when run via `func host start`.