namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain;

/// <summary>
/// Result of posting a work-order status update to FSCM.
/// </summary>
public sealed record WorkOrderStatusUpdateResponse(bool IsSuccess, int StatusCode, string? ResponseBody);
