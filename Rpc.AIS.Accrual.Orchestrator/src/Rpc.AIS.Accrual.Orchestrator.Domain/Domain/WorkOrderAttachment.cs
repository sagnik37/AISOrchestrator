namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain;

/// <summary>
/// A document attachment related to a Work Order in Field Service.
/// </summary>
public sealed record WorkOrderAttachment(
    string FileName,
    string FileUrl,
    string? Confidentiality);
