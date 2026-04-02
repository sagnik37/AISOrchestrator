using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Retrieves per-legal-entity integration parameters from FSCM.
/// </summary>
public interface IFscmLegalEntityIntegrationParametersClient
{
    /// <summary>
    /// Fetches journal name identifiers for the given legal entity (DataAreaId).
    /// </summary>
    Task<LegalEntityJournalNames> GetJournalNamesAsync(RunContext ctx, string dataAreaId, CancellationToken ct);
}
