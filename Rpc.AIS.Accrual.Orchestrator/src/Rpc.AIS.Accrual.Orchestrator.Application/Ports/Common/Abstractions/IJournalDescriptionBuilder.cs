namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

public interface IJournalDescriptionBuilder
{
    string Build(string jobId, string subProjectId, string action);
}
