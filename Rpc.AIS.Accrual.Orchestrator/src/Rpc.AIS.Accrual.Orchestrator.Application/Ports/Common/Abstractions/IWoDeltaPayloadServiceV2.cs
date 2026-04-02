using System;
using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Delta builder with explicit options (baseline scoping + target-mode overrides).
/// This is additive to <see cref="IWoDeltaPayloadService"/>.
/// </summary>
public interface IWoDeltaPayloadServiceV2
{
    Task<WoDeltaPayloadBuildResult> BuildDeltaPayloadAsync(
        RunContext context,
        string fsaWoPayloadJson,
        DateTime todayUtc,
        WoDeltaBuildOptions options,
        CancellationToken ct);
}

public sealed record WoDeltaBuildOptions(
    string? BaselineSubProjectId,
    WoDeltaTargetMode TargetMode);

public enum WoDeltaTargetMode
{
    /// <summary>Normal delta semantics (current behavior).</summary>
    Normal = 0,

    /// <summary>
    /// Force the output payload to drive the target subproject to zero by treating all lines as inactive.
    /// Used for cancellation/reversal scenarios.
    /// </summary>
    CancelToZero = 1
}
