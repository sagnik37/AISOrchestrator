using System;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Resilience;

/// <summary>
/// Enterprise-grade (but dependency-free) HTTP resilience options.
/// This is intentionally simple and deterministic to avoid changing behavior unexpectedly.
/// </summary>
public sealed class HttpResilienceOptions
{
    public const string SectionName = "Ais:HttpResilience";

    /// <summary>Maximum number of attempts for a single request (initial try + retries).</summary>
    public int MaxAttempts { get; init; } = 5;

    /// <summary>Base delay for exponential backoff.</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Maximum delay cap for backoff.</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum consecutive failures before opening the circuit.</summary>
    public int CircuitBreakerFailureThreshold { get; init; } = 10;

    /// <summary>How long the circuit remains open before allowing a trial request.</summary>
    public TimeSpan CircuitBreakerOpenDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Whether to apply a small random jitter to delays.</summary>
    public bool UseJitter { get; init; } = true;

    /// <summary>
    /// Operation names that must NOT be retried because the underlying request is non-idempotent.
    /// 
    /// Example: FSCM journal create/post. Retrying these can produce duplicate journals if the first
    /// attempt actually succeeded server-side but the client timed out or the response was lost.
    /// </summary>
    public string[] NoRetryOperations { get; init; } = new[]
    {
        // Posting workflow steps
        "FSCM_JOURNAL_CREATE",
        "FSCM_JOURNAL_POST",
        "FSCM_CreateSubProject"
    };
}
