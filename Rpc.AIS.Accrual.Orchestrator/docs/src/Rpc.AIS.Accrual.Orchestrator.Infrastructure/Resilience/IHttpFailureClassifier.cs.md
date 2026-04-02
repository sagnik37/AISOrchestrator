# HTTP Failure Classifier Feature Documentation

## Overview

The HTTP Failure Classifier centralizes logic that determines whether an HTTP response or exception should trigger a retry.

It ensures consistent retry behavior across all HTTP clients in the resilience infrastructure.

By handling retryable status codes and transient exceptions in one place, it prevents divergent retry logic and potential unintended side effects.

## Component Structure

### IHttpFailureClassifier (src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Resilience/IHttpFailureClassifier.cs)

- **Purpose:**

Defines a contract for classifying HTTP outcomes (responses or exceptions) as retryable or non-retryable.

- **Methods:**

| Method | Description | Returns |
| --- | --- | --- |
| `IsRetryable(HttpResponseMessage response)` | Determines if an HTTP response status warrants a retry. | `bool` |
| `IsRetryable(Exception ex)` | Determines if a thrown exception warrants a retry. | `bool` |


```csharp
public interface IHttpFailureClassifier
{
    bool IsRetryable(HttpResponseMessage response);
    bool IsRetryable(Exception ex);
}
```

---

### DefaultHttpFailureClassifier (src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Resilience/IHttpFailureClassifier.cs)

- **Purpose:**

Provides a default implementation tuned for FSCM-style integrations. It retries on common transient errors and throttling, but avoids retrying client errors that are likely permanent.

- **Retryable HTTP Status Codes:**- 408 (Request Timeout)
- 429 (Too Many Requests)
- Any 5xx (Server errors)

- **Retryable Exceptions:**- `HttpRequestException`
- `TaskCanceledException` (including timeouts)

- **Key Methods:**

```csharp
  public sealed class DefaultHttpFailureClassifier : IHttpFailureClassifier
  {
      public bool IsRetryable(HttpResponseMessage response)
      {
          if (response is null) return true;
          var code = (int)response.StatusCode;
          if (code == 408) return true;
          if (code == 429) return true;
          if (code >= 500 && code <= 599) return true;
          return false;
      }

      public bool IsRetryable(Exception ex)
      {
          if (ex is null) return false;
          if (ex is HttpRequestException) return true;
          if (ex is TaskCanceledException) return true;
          return false;
      }
  }
```

- **Behavior Highlights:**- A null `HttpResponseMessage` is treated as retryable.
- All 5xx server errors are considered transient and retriable.
- Most 4xx errors (other than 408/429) are not retried to avoid needless repeats of bad requests.
- Network-level failures and cancellations trigger retries to survive transient connectivity issues.

## Error Handling

- **Centralized Classification:**

All HTTP resilience components (e.g., retry policies, circuit breakers) rely on this classifier to decide whether to retry or fail fast.

- **Failure Flow:**1. An HTTP call completes or throws an exception.
2. The resilience executor invokes `IsRetryable(...)`.
3. If `true`, a retry is scheduled according to configured back-off.
4. If `false`, the exception or response is propagated immediately.

## Key Classes Reference

| Class | Location | Responsibility |
| --- | --- | --- |
| `IHttpFailureClassifier` | src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Resilience/IHttpFailureClassifier.cs | Defines retryable check contract for HTTP outcomes. |
| `DefaultHttpFailureClassifier` | src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Resilience/IHttpFailureClassifier.cs (below) | Implements default retry criteria for responses/exceptions. |


## Dependencies

- System.Net.Http.HttpResponseMessage
- System.Exception hierarchy

The classifier is used by `ResilientHttpExecutor` to drive retry logic consistently across the system.