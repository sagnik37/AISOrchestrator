using System.Collections.Generic;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Processing;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services.Handlers;

public interface IPostingAuditWriter
{
    void WritePostingAudit(
        RunAuditSummary audit,
        IReadOnlyList<PostResult> results,
        IReadOnlyDictionary<JournalType, IReadOnlyList<WoPayloadCandidateWorkOrder>> candidatesByJournal);

    void WriteExceptionAudit(RunAuditSummary audit, System.Exception ex);
}
