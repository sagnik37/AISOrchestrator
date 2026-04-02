using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;

using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Http;

public static class HttpPolicies
{
    public static IAsyncPolicy<HttpResponseMessage> BuildTimeoutPolicy(TimeSpan timeout)
        => Policy.TimeoutAsync<HttpResponseMessage>(timeout, TimeoutStrategy.Optimistic);

    public static IAsyncPolicy<HttpResponseMessage> BuildRetryPolicy(
        IServiceProvider sp,
        string clientName,
        HttpPolicyOptions.CategoryOptions opt)
    {
        var loggerFactory = sp.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
        var log = loggerFactory?.CreateLogger($"HTTP:{clientName}");

        var delays = Enumerable.Range(1, opt.Retries)
            .Select(i =>
            {
                // exponential with cap; jitter from +-20%
                var exp = Math.Min(opt.MaxBackoffSeconds, Math.Pow(2, i));
                var baseDelay = TimeSpan.FromSeconds(exp);

                var jitterPct = 0.2;
                var jitter = 1.0 + (Random.Shared.NextDouble() * 2.0 - 1.0) * jitterPct;
                var jittered = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * jitter);

                return jittered < TimeSpan.Zero ? TimeSpan.Zero : jittered;
            })
            .ToArray();

        return HttpPolicyExtensions
            .HandleTransientHttpError()               // 5xx + 408 + HttpRequestException
            .Or<TimeoutRejectedException>()           // Polly timeout
            .OrResult(r => (int)r.StatusCode == 429)   // throttling
            .WaitAndRetryAsync(
                delays,
                onRetryAsync: async (outcome, delay, retryAttempt, ctx) =>
                {
                    if (outcome.Result is not null && (int)outcome.Result.StatusCode == 429)
                    {
                        var ra = GetRetryAfter(outcome.Result);
                        if (ra is not null) delay = ra.Value;
                    }

                    log?.LogWarning(
                        "HTTP retry {RetryAttempt}/{MaxRetries} for {Client}. Status={Status} DelayMs={DelayMs}",
                        retryAttempt, opt.Retries, clientName,
                        outcome.Result?.StatusCode.ToString() ?? outcome.Exception?.GetType().Name,
                        (int)delay.TotalMilliseconds);

                    await Task.CompletedTask;
                });
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage resp)
    {
        if (resp.Headers.RetryAfter is null) return null;

        if (resp.Headers.RetryAfter.Delta is { } delta)
            return delta;

        if (resp.Headers.RetryAfter.Date is { } date)
        {
            var diff = date - DateTimeOffset.UtcNow;
            return diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
        }

        return null;
    }
}
