namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Defines i ais logger behavior.
/// </summary>
public interface IAisLogger
{
    Task InfoAsync(string runId, string step, string message, object? data, CancellationToken ct);
    Task WarnAsync(string runId, string step, string message, object? data, CancellationToken ct);
    Task ErrorAsync(string runId, string step, string message, Exception? ex, object? data, CancellationToken ct);
}
