using System;
using System.Net;
using System.Net.Http;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Resilience;

/// <summary>
/// Determines whether an HTTP outcome is retryable.
/// Keep the logic centralized to avoid divergent retry behavior across clients.
/// </summary>
public interface IHttpFailureClassifier
{
    bool IsRetryable(HttpResponseMessage response);
    bool IsRetryable(Exception ex);
}

/// <summary>
/// Default FSCM-friendly classifier:
/// - Retry 408, 429, and 5xx
/// - Do not retry most 4xx (other than 408/429)
/// - Retry on transient transport exceptions.
/// </summary>
public sealed class DefaultHttpFailureClassifier : IHttpFailureClassifier
{
    public bool IsRetryable(HttpResponseMessage response)
    {
        if (response is null) return true;

        var code = (int)response.StatusCode;

        if (code == 408) return true; // RequestTimeout
        if (code == 429) return true; // TooManyRequests

        // 5xx are transient (except some not, but treat as retryable)
        if (code >= 500 && code <= 599) return true;

        return false;
    }

    public bool IsRetryable(Exception ex)
    {
        if (ex is null) return false;

        // Common transient network failures
        if (ex is HttpRequestException) return true;
        if (ex is TaskCanceledException) return true; // timeout / cancellation (caller ct may also cause this)

        return false;
    }
}
