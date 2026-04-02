using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services;

public sealed class JournalDescriptionBuilder : IJournalDescriptionBuilder
{
    public string Build(string jobId, string subProjectId, string action)
    {
        jobId ??= string.Empty;
        subProjectId ??= string.Empty;
        action ??= string.Empty;

        return $"{jobId} - {subProjectId} - {action}";
    }
}
