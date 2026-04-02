using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Processing;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

public interface IProcessingAuditService
{
    RunAuditSummary CreateSummary(RunContext context);

    void AddSuccess(
        RunAuditSummary summary,
        string workOrderNumber,
        string stage,
        string? workOrderGuid = null,
        string? journalType = null);

    void AddFailure(
        RunAuditSummary summary,
        string workOrderNumber,
        string stage,
        string reason,
        string? workOrderGuid = null,
        string? journalType = null);

    void LogSummary(RunAuditSummary summary);
}
