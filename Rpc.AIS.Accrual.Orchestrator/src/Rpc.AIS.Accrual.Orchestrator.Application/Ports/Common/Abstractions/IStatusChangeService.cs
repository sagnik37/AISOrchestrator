using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Defines i status change service behavior.
/// </summary>
public interface IStatusChangeService
{
    Task HandleAsync(StatusChangeRequest request, CancellationToken ct);
}
