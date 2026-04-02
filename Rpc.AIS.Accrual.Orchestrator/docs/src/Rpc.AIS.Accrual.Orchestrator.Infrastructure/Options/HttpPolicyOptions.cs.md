# HttpPolicyOptions

## Overview

`HttpPolicyOptions` centralizes HTTP resilience settings for the application. It defines per-category timeouts, retry counts, and back-off caps. These options drive how `HttpClient` instances apply timeout and retry policies for **Dataverse** and **FSCM** integrations.

## Configuration Section ⚙️

The options bind to the **HttpPolicies** section of your configuration (appsettings.json, Azure App Configuration, etc.).

Use the `SectionName` constant to reference the correct section name in code.

```csharp
builder.Services
    .Configure<HttpPolicyOptions>(
        Configuration.GetSection(HttpPolicyOptions.SectionName));
```

### appsettings.json example

```json
{
  "HttpPolicies": {
    "Dataverse": {
      "TimeoutSeconds": 60,
      "Retries": 5,
      "MaxBackoffSeconds": 30
    },
    "Fscm": {
      "TimeoutSeconds": 60,
      "Retries": 5,
      "MaxBackoffSeconds": 30
    }
  }
}
```

## Properties

- **SectionName** (`string`): `"HttpPolicies"` — config section key.
- **Dataverse** (`CategoryOptions`, init-only)
- **Fscm** (`CategoryOptions`, init-only)

Each category shares the same nested settings shape.

## CategoryOptions 🔧

Defines timeout and retry parameters for one HTTP category.

| Property | Type | Default | Range | Description |
| --- | --- | --- | --- | --- |
| TimeoutSeconds | int | 60 | 1 – 600 | Total timeout (seconds) per HTTP attempt. |
| Retries | int | 5 | 0 – 20 | Max retry count after the initial attempt. |
| MaxBackoffSeconds | int | 30 | 1 – 300 | Maximum back-off delay (seconds) between retries. |


```csharp
public sealed class CategoryOptions
{
    [Range(1, 600)]
    public int TimeoutSeconds { get; init; } = 60;

    [Range(0, 20)]
    public int Retries { get; init; } = 5;

    [Range(1, 300)]
    public int MaxBackoffSeconds { get; init; } = 30;
}
```

## Usage 📦

1. **Binding**

The options class binds from configuration via `IOptions<HttpPolicyOptions>`.

1. **Policy Application**- `Rpc.AIS.Accrual.Orchestrator.Infrastructure.Resilience.HttpPolicies` consumes these values to set `HttpClient.Timeout` and build retry policies.
- `HttpClientRegistrationExtensions` uses the same options to attach timeout and retry handlers during DI registration.

### Example: Applying Timeout

```csharp
var opts = sp.GetRequiredService<IOptions<HttpPolicyOptions>>().Value;
var dataverseOpt = opts.Dataverse;
httpClient.Timeout = TimeSpan.FromSeconds(dataverseOpt.TimeoutSeconds);
```

### Example: Creating Retry Policy

```csharp
var opt = opts.Fscm;
var retryPolicy = policies.CreateRetryPolicy(opt, logger, "FSCM");
// uses Retries and MaxBackoffSeconds internally
```

## Related Components

| Class | Responsibility |
| --- | --- |
| HttpPolicies (`Infrastructure/Resilience/`) | Builds timeout and retry policies from these options. |
| HttpClientRegistrationExtensions (`Infrastructure`) | Registers `HttpClient` with handlers using these options. |


---

*End of documentation for `HttpPolicyOptions.cs`*