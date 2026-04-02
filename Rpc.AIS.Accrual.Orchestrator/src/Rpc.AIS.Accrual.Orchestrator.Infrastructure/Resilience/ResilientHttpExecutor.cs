using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Resilience;

/// <summary>
/// Dependency-free resilience executor (retry + basic circuit breaker).
/// </summary>
public sealed class ResilientHttpExecutor : IResilientHttpExecutor
{
    private readonly IHttpFailureClassifier _classifier;
    private readonly HttpResilienceOptions _opt;
    private readonly ILogger<ResilientHttpExecutor> _logger;

    // Simple global circuit breaker state (per-process).
    private int _consecutiveFailures;
    private DateTimeOffset? _openUntil;

    public ResilientHttpExecutor(
        IHttpFailureClassifier classifier,
        IOptions<HttpResilienceOptions> options,
        ILogger<ResilientHttpExecutor> logger)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _opt = (options?.Value) ?? new HttpResilienceOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        Func<HttpRequestMessage> requestFactory,
        RunContext ctx,
        string operationName,
        CancellationToken ct)
    {
        if (http is null) throw new ArgumentNullException(nameof(http));
        if (requestFactory is null) throw new ArgumentNullException(nameof(requestFactory));
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        operationName = TelemetryConventions.NormalizeDependencyOperation(operationName);

        // Circuit breaker gate
        var openUntil = _openUntil;
        if (openUntil is not null && openUntil.Value > DateTimeOffset.UtcNow)
            throw new HttpRequestException($"Circuit open for operation '{operationName}' until {openUntil:O}.");

        Exception? last = null;

        var maxAttempts = Math.Max(1, _opt.MaxAttempts);

        // Hard rule: never retry HTTP POST (non-idempotent). This prevents duplicate side effects
        // (e.g., duplicate journal creates/posts) when the first attempt succeeded server-side
        // but the response was lost or the client timed out.
        var isPost = false;
        try
        {
            using var probe = requestFactory();
            isPost = probe.Method == HttpMethod.Post;
        }
        catch (Exception ex)
        {
            // If we can't probe safely, fall back to configured behavior.
            ThrottledLog.Debug(
                _logger,
                ctx,
                key: "ResilientHttpExecutor.ProbeMethod",
                message: "Failed to probe request method; falling back to configured behavior.",
                ex: ex);
        }

        if (isPost)
        {
            if (maxAttempts != 1)
            {
                _logger.LogInformation(
                    "HTTP retries disabled for POST request. Op={Op} ConfiguredMaxAttempts={ConfiguredMaxAttempts} RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggeredBy={TriggeredBy}",
                    operationName, maxAttempts, ctx.RunId, ctx.CorrelationId, ctx.SourceSystem, ctx.TriggeredBy);
            }

            maxAttempts = 1;
        }

        // IMPORTANT:
        // Some operations are non-idempotent (e.g., FSCM journal create/post).
        // Retrying them can create duplicate journals if the first attempt succeeded server-side
        // but the response was lost or the client timed out.
        if (IsNoRetryOperation(operationName))
        {
            if (maxAttempts != 1)
            {
                _logger.LogInformation(
                    "HTTP retries disabled for non-idempotent operation. Op={Op} ConfiguredMaxAttempts={ConfiguredMaxAttempts} RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggeredBy={TriggeredBy}",
                    operationName, maxAttempts, ctx.RunId, ctx.CorrelationId, ctx.SourceSystem, ctx.TriggeredBy);
            }

            maxAttempts = 1;
        }

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = requestFactory();

            _logger.LogInformation(
                "HTTP dependency begin. Op={Op} Stage={Stage} Outcome={Outcome} RetryAttempt={RetryAttempt} Method={Method} Uri={Uri} RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggeredBy={TriggeredBy}",
                operationName,
                "DependencyBegin",
                TelemetryConventions.Outcomes.Accepted,
                attempt,
                request.Method,
                request.RequestUri,
                ctx.RunId,
                ctx.CorrelationId,
                ctx.SourceSystem,
                ctx.TriggeredBy);

            try
            {
                var sw = Stopwatch.StartNew();
                var resp = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                sw.Stop();

                if (_classifier.IsRetryable(resp) && attempt < maxAttempts)
                {
                    string? bodySnippet = null;
                    try
                    {
                        if (resp.Content is not null)
                        {
                            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(body))
                                bodySnippet = body.Length <= 1200 ? body : body.Substring(0, 1200);
                        }
                    }
                    catch (Exception ex)
                    {
                        // best effort only
                        ThrottledLog.Debug(
                            _logger,
                            ctx,
                            key: "ResilientHttpExecutor.ReadBodySnippet",
                            message: "Failed to read retryable response body snippet (best-effort).",
                            ex: ex);
                    }

                    if (!string.IsNullOrWhiteSpace(bodySnippet))
                    {
                        _logger.LogWarning(
                            "HTTP retryable response body snippet. Op={Op} Attempt={Attempt}/{MaxAttempts} Status={Status} Snippet={Snippet} RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggeredBy={TriggeredBy}",
                            operationName,
                            attempt,
                            maxAttempts,
                            (int)resp.StatusCode,
                            bodySnippet,
                            ctx.RunId,
                            ctx.CorrelationId,
                            ctx.SourceSystem,
                            ctx.TriggeredBy);
                    }

                    await RegisterRetryAsync(ctx, operationName, attempt, maxAttempts, sw.ElapsedMilliseconds, ((int)resp.StatusCode).ToString(), null).ConfigureAwait(false);
                    resp.Dispose();
                    await DelayBeforeRetryAsync(attempt, ct).ConfigureAwait(false);
                    continue;
                }

                RegisterSuccess();
                _logger.LogInformation(
                    "HTTP dependency completed. Op={Op} Stage={Stage} Outcome={Outcome} Status={Status} ElapsedMs={ElapsedMs} RetryAttempt={RetryAttempt} RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggeredBy={TriggeredBy}",
                    operationName,
                    "DependencyEnd",
                    TelemetryConventions.Outcomes.Success,
                    (int)resp.StatusCode,
                    sw.ElapsedMilliseconds,
                    attempt,
                    ctx.RunId,
                    ctx.CorrelationId,
                    ctx.SourceSystem,
                    ctx.TriggeredBy);
                return resp;
            }
            catch (Exception ex) when (_classifier.IsRetryable(ex) && attempt < maxAttempts)
            {
                last = ex;
                await RegisterRetryAsync(ctx, operationName, attempt, maxAttempts, null, null, ex).ConfigureAwait(false);
                await DelayBeforeRetryAsync(attempt, ct).ConfigureAwait(false);
                continue;
            }
            catch (Exception ex)
            {
                last = ex;
                RegisterFailureFinal(ctx, operationName, ex);
                throw;
            }
        }

        RegisterFailureFinal(ctx, operationName, last);
        throw last ?? new HttpRequestException("HTTP call failed without exception.");
    }

    private bool IsNoRetryOperation(string operationName)
    {
        if (string.IsNullOrWhiteSpace(operationName)) return false;

        var list = _opt.NoRetryOperations;
        if (list is null || list.Length == 0) return false;

        foreach (var op in list)
        {
            if (!string.IsNullOrWhiteSpace(op) && string.Equals(op.Trim(), operationName.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void RegisterSuccess()
    {
        _consecutiveFailures = 0;
        _openUntil = null;
    }

    private void RegisterFailureFinal(RunContext? ctx, string? op, Exception? ex)
    {
        var fail = Interlocked.Increment(ref _consecutiveFailures);
        var failureCategory = TelemetryConventions.ClassifyFailure(ex);
        var errorType = ex?.GetType().Name ?? "Unknown";

        _logger.LogError(
            ex,
            "HTTP dependency failed. Op={Op} Stage={Stage} Outcome={Outcome} FailureCategory={FailureCategory} ErrorType={ErrorType} IsRetryable={IsRetryable} RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggeredBy={TriggeredBy} ConsecutiveFailures={ConsecutiveFailures}",
            op ?? "HTTP",
            "DependencyEnd",
            TelemetryConventions.Outcomes.Failed,
            failureCategory,
            errorType,
            false,
            ctx?.RunId,
            ctx?.CorrelationId,
            ctx?.SourceSystem,
            ctx?.TriggeredBy,
            fail);

        if (fail >= _opt.CircuitBreakerFailureThreshold)
        {
            _openUntil = DateTimeOffset.UtcNow.Add(_opt.CircuitBreakerOpenDuration);
            _logger.LogWarning(
                "Circuit opened after {Failures} consecutive failures. Op={Op} OpenUntil={OpenUntil:o} Stage={Stage} Outcome={Outcome} FailureCategory={FailureCategory} RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggeredBy={TriggeredBy}",
                fail,
                op ?? "HTTP",
                _openUntil,
                "CircuitOpen",
                TelemetryConventions.Outcomes.Failed,
                failureCategory,
                ctx?.RunId,
                ctx?.CorrelationId,
                ctx?.SourceSystem,
                ctx?.TriggeredBy);
        }
    }

    private Task RegisterRetryAsync(RunContext ctx, string op, int attempt, int maxAttempts, long? elapsedMs, string? status, Exception? ex)
    {
        var fail = Interlocked.Increment(ref _consecutiveFailures);

        var failureCategory = TelemetryConventions.ClassifyFailure(ex, int.TryParse(status, out var statusCodeInt) ? (HttpStatusCode?)statusCodeInt : null);
        var errorType = ex?.GetType().Name ?? (status ?? "HttpStatus");
        _logger.LogWarning(
            "HTTP retryable failure. Op={Op} Stage={Stage} Outcome={Outcome} FailureCategory={FailureCategory} ErrorType={ErrorType} IsRetryable={IsRetryable} RetryAttempt={RetryAttempt} Attempt={Attempt}/{MaxAttempts} Status={Status} ElapsedMs={ElapsedMs} RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggeredBy={TriggeredBy} Failures={Failures} Error={Error}",
            op, "DependencyRetry", TelemetryConventions.Outcomes.Retryable, failureCategory, errorType, true, attempt, attempt, maxAttempts, status ?? "<ex>", elapsedMs, ctx.RunId, ctx.CorrelationId, ctx.SourceSystem, ctx.TriggeredBy, fail, ex?.Message);

        if (fail >= _opt.CircuitBreakerFailureThreshold)
        {
            _openUntil = DateTimeOffset.UtcNow.Add(_opt.CircuitBreakerOpenDuration);
            _logger.LogWarning("Circuit opened. Op={Op} OpenUntil={OpenUntil:o} Stage={Stage} Outcome={Outcome} RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggeredBy={TriggeredBy}", op, _openUntil, "CircuitOpen", TelemetryConventions.Outcomes.Failed, ctx.RunId, ctx.CorrelationId, ctx.SourceSystem, ctx.TriggeredBy);
        }

        return Task.CompletedTask;
    }

    private async Task DelayBeforeRetryAsync(int attempt, CancellationToken ct)
    {
        // attempt is 1-based
        var pow = Math.Min(attempt, 8);
        var delayMs = _opt.BaseDelay.TotalMilliseconds * Math.Pow(2, pow - 1);
        delayMs = Math.Min(delayMs, _opt.MaxDelay.TotalMilliseconds);

        if (_opt.UseJitter)
        {
            var jitter = RandomNumberGenerator.GetInt32(-200, 201) / 1000.0; // -0.2..0.2
            delayMs = delayMs * (1.0 + jitter);
        }

        if (delayMs < 0) delayMs = 0;
        await Task.Delay(TimeSpan.FromMilliseconds(delayMs), ct).ConfigureAwait(false);
    }
}
