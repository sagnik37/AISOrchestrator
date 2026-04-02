using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Custom endpoint to update FSCM project/subproject status/stage.
/// </summary>
public interface IFscmProjectStatusClient
{
    /// <summary>
    /// New contract (preferred): update project status for a specific WO + SubProjectId.
    /// Payload contract:
    /// {
    ///   "_request": {
    ///     "WOList": [
    ///       {
    ///         "Company": "425",
    ///         "SubProjectId": "425-P0000001-00001",
    ///         "WorkOrderGUID": "{...}",
    ///         "WorkOrderID": "...",
    ///         "Status": 5|6
    ///       }
    ///     ]
    ///   }
    /// }
    /// </summary>
    Task<FscmProjectStatusUpdateResult> UpdateAsync(
        RunContext ctx,
        string company,
        string subProjectId,
        Guid workOrderGuid,
        string workOrderId,
        int status,
        CancellationToken ct);

    /// <summary>
    /// Legacy overload: some callers historically used a GUID SubProjectId.
    /// Preserved for backward compatibility.
    /// </summary>
    Task<FscmProjectStatusUpdateResult> UpdateAsync(RunContext ctx, Guid subprojectId, string newStatus, CancellationToken ct);

    /// <summary>
    /// Legacy overload: older callers used a textual status ("Posted", "Cancelled", ...).
    /// Preserved for backward compatibility.
    /// </summary>
    Task<FscmProjectStatusUpdateResult> UpdateAsync(RunContext ctx, string company, string subProjectId, string newStatus, CancellationToken ct);
}

public sealed record FscmProjectStatusUpdateResult(bool IsSuccess, int HttpStatus, string? Body);
