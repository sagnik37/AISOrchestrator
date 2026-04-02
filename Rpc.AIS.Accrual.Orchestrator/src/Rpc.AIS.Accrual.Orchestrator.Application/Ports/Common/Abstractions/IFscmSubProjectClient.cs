using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Defines i fscm sub project client behavior.
/// </summary>
public interface IFscmSubProjectClient
{
    Task<SubProjectCreateResult> CreateSubProjectAsync(
        RunContext context,
        SubProjectCreateRequest request,
        CancellationToken ct);
}
