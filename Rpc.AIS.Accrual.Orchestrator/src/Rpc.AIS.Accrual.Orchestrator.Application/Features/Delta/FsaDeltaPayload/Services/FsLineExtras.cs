namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload;

public sealed record FsLineExtras(
    string? Currency,
    string? WorkerNumber,
    string? WarehouseIdentifier,
    string? SiteId,
    int? LineNum,
    string? OperationsDate) //  NEW
{
    public bool HasAny()
        => !string.IsNullOrWhiteSpace(Currency)
        || !string.IsNullOrWhiteSpace(WorkerNumber)
        || !string.IsNullOrWhiteSpace(WarehouseIdentifier)
        || !string.IsNullOrWhiteSpace(SiteId)
        || LineNum.HasValue
        || !string.IsNullOrWhiteSpace(OperationsDate); //  NEW
}
