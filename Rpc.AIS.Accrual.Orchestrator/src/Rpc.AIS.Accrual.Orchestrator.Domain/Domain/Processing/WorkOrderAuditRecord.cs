namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.Processing;

public sealed class WorkOrderAuditRecord
{
    public string WorkOrderNumber { get; init; } = string.Empty;
    public string? WorkOrderGuid { get; init; }
    public bool Success { get; init; }
    public string Stage { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public string? JournalType { get; init; }
}
