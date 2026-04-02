using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Client abstraction for sending a work-order status update payload to FSCM.
/// Payload is forwarded unchanged (raw JSON).
/// </summary>
public interface IWorkOrderStatusUpdateClient
{
    Task<WorkOrderStatusUpdateResponse> UpdateAsync(string rawJsonBody, CancellationToken ct);
    // NEW (preferred)
    Task<WorkOrderStatusUpdateResponse> UpdateAsync(RunContext context, string rawJsonBody, CancellationToken ct);
}
