# Service Dependencies Configuration for func-FsaFscm-WOTransaction-AIS-dev-EastUs Zip Deploy

## Overview

This JSON file declares external service dependencies for the Azure Function **func-FsaFscm-WOTransaction-AIS-dev-EastUs**.

It guides the Zip Deploy process to wire up Application Insights under the hood, ensuring telemetry is automatically configured at runtime.

By specifying a named dependency (`appInsights1`), this configuration instructs Azure Functions to:

- Retrieve the Application Insights connection string from the **AzureAppSettings** secret store
- Link to the correct resource ID in the target subscription and resource group
- Expose the instrumentation key via the `APPLICATIONINSIGHTS_CONNECTION_STRING` environment variable

---

## Configuration Structure

```json
{
  "dependencies": {
    "appInsights1": {
      "secretStore": "AzureAppSettings",
      "resourceId": "/subscriptions/[parameters('subscriptionId')]/resourceGroups/[parameters('resourceGroupName')]/providers/microsoft.insights/components/app-fs-fscm-intg-woprojects",
      "type": "appInsights.azure",
      "connectionId": "APPLICATIONINSIGHTS_CONNECTION_STRING",
      "dynamicId": null
    }
  }
}
```

### Top-Level Sections

- **dependencies**

An object mapping logical dependency names to their deployment metadata.

---

## appInsights1 Dependency Details 🔧

This entry configures a single Application Insights component:

| Property | Type | Description |
| --- | --- | --- |
| **secretStore** | string | Source for the secret containing the connection string. Here, `AzureAppSettings` refers to App Settings. |
| **resourceId** | string | ARM resource identifier for the Application Insights instance. |
| **type** | string | Dependency type. For App Insights integration use `appInsights.azure`. |
| **connectionId** | string | Environment variable name under which the instrumentation key will be exposed. |
| **dynamicId** | null | When non-null, indicates a runtime-computed resource. Not used here. |


---

## Deployment Context

- **Zip Deploy Consumption**

During function app deployment, Azure reads this file to automatically configure linked services.

The `appInsights1` settings are transformed into platform-managed bindings that inject the instrumentation key.

- **Parameterization**

The `resourceId` path uses ARM template parameters:

- `[parameters('subscriptionId')]`
- `[parameters('resourceGroupName')]`

This enables reuse across environments (dev, sit, prod) by supplying different parameter values at deployment time.

---

## Usage Flow

1. **Deploy**

The Zip Deploy process includes this JSON alongside compiled function code.

1. **Bind**

Azure Functions build the binding to Application Insights, retrieving the connection string from App Settings.

1. **Instrument**

At startup, the function runtime picks up `APPLICATIONINSIGHTS_CONNECTION_STRING` and initializes telemetry automatically.

```bash
# Example: inspect deployed setting in Azure CLI
az functionapp config appsettings list \
  --name <functionAppName> \
  --resource-group <rgName> \
  --query "[?name=='APPLICATIONINSIGHTS_CONNECTION_STRING']"
```

---

## Card

```card
{
    "title": "App Insights Binding",
    "content": "This file ensures Application Insights is wired via the FUNCTIONS_EXTENSION_VERSION mechanism."
}
```