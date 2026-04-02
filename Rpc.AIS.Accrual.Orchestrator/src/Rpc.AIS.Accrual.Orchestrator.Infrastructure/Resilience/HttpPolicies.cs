using System;
using System.Net;
using System.Net.Http;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Polly;
using Polly.Extensions.Http;

using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Resilience;

/// <summary>
/// Centralized HTTP resilience policy builder.
/// Applies timeout + retry with exponential backoff.
/// </summary>
public sealed class HttpPolicies
{
    private readonly HttpPolicyOptions _options;

    public HttpPolicies(IOptions<HttpPolicyOptions> options)
    {
        _options = options?.Value
            ?? throw new ArgumentNullException(nameof(options));
    }

    public void ApplyTimeout(HttpClient client, HttpPolicyOptions.CategoryOptions category)
    {
        client.Timeout = TimeSpan.FromSeconds(category.TimeoutSeconds);
    }

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
}
