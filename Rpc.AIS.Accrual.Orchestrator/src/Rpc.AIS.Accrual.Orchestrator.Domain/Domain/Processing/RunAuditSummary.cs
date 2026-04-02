using System.Collections.Generic;
using System.Linq;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.Processing;

public sealed class RunAuditSummary
{
    public string RunId { get; init; } = string.Empty;
    public string? CorrelationId { get; init; }
    public string? TriggeredBy { get; init; }

    public List<WorkOrderAuditRecord> Records { get; } = new();

    public int Total => Records.Count;
    public int SuccessCount => Records.Count(x => x.Success);
    public int FailureCount => Records.Count(x => !x.Success);
}
