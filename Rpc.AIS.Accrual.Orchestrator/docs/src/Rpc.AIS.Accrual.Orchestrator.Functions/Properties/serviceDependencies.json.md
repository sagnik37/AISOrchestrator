# Service Dependencies Configuration

## Overview

This configuration file declares external services required by the Azure Function App. It enables the Azure Functions deployment tooling to provision or link resources—most notably Application Insights—for telemetry and observability purposes.

## File Location and Purpose

- **Path:** `src/Rpc.AIS.Accrual.Orchestrator.Functions/Properties/serviceDependencies.json`
- **Format:** JSON
- **Responsibility:** Defines named service dependencies that the Azure Functions Core Tools will process during Zip Deploy.

## Structure

```json
{
  "dependencies": {
    "appInsights1": {
      "type": "appInsights",
      "connectionId": "APPLICATIONINSIGHTS_CONNECTION_STRING",
      "dynamicId": null
    }
  }
}
```

- **dependencies**- Root object mapping each dependency by a unique key.
- **type**- Specifies the kind of Azure service to bind (e.g., `appInsights`).
- **connectionId**- The name of the App Setting holding the service’s connection string or instrumentation key.
- **dynamicId**- Reserved for dynamic resource‐ID resolution; a value of `null` indicates a static mapping.

## Dependency Definitions 📦

| Dependency Name | Type | Connection ID | Dynamic ID | Description |
| --- | --- | --- | --- | --- |
| **appInsights1** | `appInsights` | `APPLICATIONINSIGHTS_CONNECTION_STRING` | `null` | Associates the Function App with an Application Insights instance for logging and telemetry. |


## Deployment Integration 🔧

During a **Zip Deploy**, the Azure Functions Core Tools will:

- Read this file to identify required service dependencies.
- Provision or link the specified Application Insights resource.
- Create or update the `APPLICATIONINSIGHTS_CONNECTION_STRING` App Setting on the Function App.
- Enable automatic telemetry injection at runtime.

## Key Properties Reference

| Property | Type | Required | Description |
| --- | --- | --- | --- |
| **dependencies** | object | Yes | Container for all named service definitions. |
| **type** | string | Yes | Service category (e.g., `appInsights`). |
| **connectionId** | string | Yes | Environment variable key for retrieving the service’s connection details. |
| **dynamicId** | string or null | No | Optional dynamic resource‐ID; `null` indicates use of a pre‐existing resource. |


## Usage in the Codebase

No code within the Function App directly reads this file at runtime. Instead, the Azure Functions host processes it during deployment to wire up the Application Insights dependency, ensuring telemetry is correctly configured without manual intervention.