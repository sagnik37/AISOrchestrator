namespace Rpc.AIS.Accrual.Orchestrator.Core.Options;

/// <summary>
/// Minimal run-time options required by the delta payload build use case.
/// Mapped from Infrastructure FsOptions at the Functions boundary.
/// </summary>
public sealed class FsaDeltaPayloadRunOptions
{
    public string? WorkOrderFilter { get; init; }
}
