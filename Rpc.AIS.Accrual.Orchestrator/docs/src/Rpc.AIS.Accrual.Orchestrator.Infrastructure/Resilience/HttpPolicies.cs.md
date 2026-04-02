# HTTP Resilience Policies Feature Documentation

## Overview

The **HTTP Resilience Policies** feature provides a centralized, configurable way to apply timeout and retry logic to all outgoing HTTP requests. By leveraging Polly’s resilience primitives, it ensures transient failures (HTTP 5xx, 408, 429 or network errors) are retried with exponential backoff, while timeouts are applied consistently according to configuration.

This approach improves overall system robustness, reduces duplicate retry logic across services, and keeps resilience behavior deterministic and easy to manage.

## Architecture Overview

```mermaid
flowchart LR
    subgraph Configuration
        HPO[HttpPolicyOptions\n(CategoryOptions)]
    end

    subgraph ResilienceLayer [Infrastructure ● Resilience Layer]
        HP[HttpPolicies]
    end

    subgraph HttpClientPipeline [HTTP Client Pipeline]
        HC[HttpClient]
        RP[IAsyncPolicy<HttpResponseMessage>]
    end

    subgraph ExternalAPI [External Service]
        API[Remote HTTP API]
    end

    HPO --> HP
    HP --> HC
    HP --> RP
    HC -->|Attach| RP
    HC --> API
```

1. **HttpPolicyOptions** loads policy settings from configuration.
2. **HttpPolicies** consumes those settings to- set `HttpClient.Timeout`
- build a Polly retry policy.
3. The **HTTP client pipeline** applies the timeout and retry policy before sending requests to an external API.

## Component Structure

### Infrastructure – Resilience Layer

#### **HttpPolicies** (`src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Resilience/HttpPolicies.cs`)

- **Purpose**

Central builder for all HTTP resilience policies: timeouts and retries.

- **Dependencies**- `IOptions<HttpPolicyOptions>` – policy configuration
- `Microsoft.Extensions.Logging.ILogger` – logging during retries
- `Polly.Extensions.Http` – transient-error detection

- **Constructor**

```csharp
  public HttpPolicies(IOptions<HttpPolicyOptions> options)
```

- Loads `HttpPolicyOptions` from DI.
- Throws `ArgumentNullException` if `options` is null.

- **Methods**

| Method | Signature | Description |
| --- | --- | --- |
| **ApplyTimeout** | `void ApplyTimeout(HttpClient client, HttpPolicyOptions.CategoryOptions category)` | Sets `client.Timeout` to `category.TimeoutSeconds`. |
| **CreateRetryPolicy** | `IAsyncPolicy<HttpResponseMessage> CreateRetryPolicy(HttpPolicyOptions.CategoryOptions category, ILogger logger, string systemName)` | Builds a Polly retry policy that: |


  - Handles 5xx, 408, `HttpRequestException`

  - Treats HTTP 429 (Too Many Requests) as retryable

  - Retries up to `category.Retries` times

  - Uses exponential backoff capped at `category.MaxBackoffSeconds`

  - Logs a warning on each retry with system name, attempt count, delay, and status code. |

#### Code Snippets

```csharp
// Constructor: capture configuration
public HttpPolicies(IOptions<HttpPolicyOptions> options)
{
    _options = options?.Value
        ?? throw new ArgumentNullException(nameof(options));
}

// Apply timeout from configuration
public void ApplyTimeout(HttpClient client, HttpPolicyOptions.CategoryOptions category)
{
    client.Timeout = TimeSpan.FromSeconds(category.TimeoutSeconds);
}

// Build Polly retry policy
public IAsyncPolicy<HttpResponseMessage> CreateRetryPolicy(
    HttpPolicyOptions.CategoryOptions category,
    ILogger logger,
    string systemName)
{
    if (category is null)
        throw new ArgumentNullException(nameof(category));

    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(
            retryCount: category.Retries,
            sleepDurationProvider: attempt =>
            {
                var delaySeconds = Math.Min(
                    Math.Pow(2, attempt),
                    category.MaxBackoffSeconds);
                return TimeSpan.FromSeconds(delaySeconds);
            },
            onRetry: (outcome, timespan, retryAttempt, _) =>
            {
                logger.LogWarning(
                    "{System} retry {RetryAttempt}/{MaxRetries}. Delay={DelaySeconds}s StatusCode={StatusCode}",
                    systemName,
                    retryAttempt,
                    category.Retries,
                    timespan.TotalSeconds,
                    outcome.Result?.StatusCode);
            });
}
```

## Configuration Models

#### **HttpPolicyOptions** (`src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Options/HttpPolicyOptions.cs`)

| Member | Type | Default | Description |
| --- | --- | --- | --- |
| `Dataverse` | `CategoryOptions` | new() | Retry & timeout settings for Dataverse HTTP clients |
| `Fscm` | `CategoryOptions` | new() | Retry & timeout settings for FSCM HTTP clients |


##### **CategoryOptions** (nested)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `TimeoutSeconds` | int | 60 | HTTP request timeout in seconds |
| `Retries` | int | 5 | Number of retry attempts (in addition to initial request) |
| `MaxBackoffSeconds` | int | 30 | Maximum delay (in seconds) in exponential backoff |


## Integration Points

- **Dependency Injection**

Register `HttpPolicies` as a singleton so it can be injected where needed:

```csharp
  services.AddSingleton<HttpPolicies>();
```

- **HttpClient Configuration**

In your `Program.cs` (Functions) or `HttpClientRegistrationExtensions`, use:

```csharp
  // Apply timeout
  policies.ApplyTimeout(client, categoryOptions);

  // Attach retry policy
  builder.AddPolicyHandler((sp, req) =>
  {
      var policies = sp.GetRequiredService<HttpPolicies>();
      var opts = sp.GetRequiredService<IOptions<HttpPolicyOptions>>().Value.Fscm;
      var logger = sp.GetRequiredService<ILoggerFactory>()
                     .CreateLogger("FscmPolicy");
      return policies.CreateRetryPolicy(opts, logger, "FSCM");
  });
```

## Error Handling Pattern

- **Null Checks**- Constructor and `CreateRetryPolicy` throw `ArgumentNullException` for missing inputs.

- **Transient Errors**- Uses `HandleTransientHttpError()` to catch 5xx, 408, and `HttpRequestException`.
- Adds `OrResult` for HTTP 429 (throttling).

- **Retry Logging**- On each retry, logs a warning with:- **systemName**
- **retryAttempt/MaxRetries**
- **delay** in seconds
- **HTTP status code**

## Dependencies

- Microsoft.Extensions.Options
- Microsoft.Extensions.Logging
- Polly (Polly.Extensions.Http)

## Testing Considerations

- Validate constructor throws on null `IOptions<HttpPolicyOptions>`.
- Verify `ApplyTimeout` sets `HttpClient.Timeout` correctly.
- Test `CreateRetryPolicy`:- Retries on simulated 5xx and 429 responses.
- Honors `Retries` count and `MaxBackoffSeconds` cap.
- Invokes `ILogger.LogWarning` with correct parameters on each retry.

## Key Classes Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| **HttpPolicies** | `Infrastructure/Resilience/HttpPolicies.cs` | Builds and applies timeout and retry policies. |
| **HttpPolicyOptions** | `Infrastructure/Options/HttpPolicyOptions.cs` | Configuration model for HTTP policy settings. |
| **CategoryOptions** | Nested in `HttpPolicyOptions` | Defines timeout, retry count, and backoff cap. |
