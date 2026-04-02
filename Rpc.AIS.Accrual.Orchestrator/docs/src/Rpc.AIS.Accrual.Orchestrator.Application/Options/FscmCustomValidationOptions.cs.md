# FSCM Custom Validation Options

## Overview

The **FscmCustomValidationOptions** class centralizes configuration for invoking a customer-specific validation API in the FSCM (Financial Supply Chain Management) system. It allows the orchestration pipeline to:

- Point to the correct **relative endpoint** under the FSCM base URL
- Control the **HTTP timeout** for remote validation calls

By exposing these settings, operators can adjust the validation endpoint path or timeout without code changes, ensuring robust integration with FSCM.

## Configuration Section

The options bind to the **Fscm:CustomValidation** section in your application settings:

- **SectionName**: `"Fscm:CustomValidation"`
- **EndpointPath**: URI path of the validation endpoint
- **TimeoutSeconds**: HTTP call timeout in seconds

## Class Definition

```csharp
namespace Rpc.AIS.Accrual.Orchestrator.Core.Options;
/// <summary>
/// Options for calling the FSCM custom validation endpoint.
/// </summary>
public sealed class FscmCustomValidationOptions
{
    public const string SectionName = "Fscm:CustomValidation";

    /// <summary>
    /// Relative endpoint path under the FSCM base URL.
    /// Example: "/api/services/AIS/Validate" or "/api/services/RpcAis/Validate"
    /// </summary>
    public string EndpointPath { get; set; } = "/api/services/AIS/Validate";

    /// <summary>
    /// HTTP timeout in seconds for the validation call.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;
}
```

## Properties Reference

| Property | Type | Default Value | Description |
| --- | --- | --- | --- |
| SectionName | string | `"Fscm:CustomValidation"` | Configuration key for binding this options class. |
| EndpointPath | string | `"/api/services/AIS/Validate"` | Relative path under the FSCM base URL for custom validation. |
| TimeoutSeconds | int | `60` | Maximum duration (in seconds) to wait for the HTTP response when calling the validation endpoint. |


## Usage in the Pipeline

1. **Binding**

In `Program.cs`, the options are added to DI and bound from configuration:

```csharp
   services.AddOptions<FscmCustomValidationOptions>()
           .Bind(cfg.GetSection(FscmCustomValidationOptions.SectionName))
           .ValidateOnStart();
```

1. **Injection**

`FscmCustomValidationClient` receives these options to construct its HTTP requests:

- Reads `EndpointPath` to build the request URI.
- Can apply `TimeoutSeconds` to its `HttpClient` instance.

Operators adjust the endpoint or timeout in appsettings or environment variables without redeploying code.

## Example Configuration

```json
{
  "Fscm:CustomValidation": {
    "EndpointPath": "/api/services/RpcAis/Validate",
    "TimeoutSeconds": 120
  },
  "Fscm": {
    "BaseUrl": "https://fscm.example.com"
  }
}
```

## Key Classes Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| FscmCustomValidationOptions | src/Rpc.AIS.Accrual.Orchestrator.Application/Options/FscmCustomValidationOptions.cs | Defines configurable settings for the FSCM custom validation API. |
| FscmCustomValidationClient | src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Adapters/Fscm/Clients/FscmCustomValidationClient.cs | Performs HTTP calls to the FSCM validation endpoint using these options. |


> ⚠️ No diagrams are included since this class solely encapsulates configuration values.