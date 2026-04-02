using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Validates an AIS Work Order payload (AIS contract) prior to posting to FSCM.
/// Implementations must be deterministic and side-effect free.
/// </summary>
public interface IWoPayloadValidationEngine
{
    /// <summary>
    /// Validates a WO payload JSON and returns a result containing invalid details and
    /// a filtered payload JSON containing ONLY valid work orders.
    /// </summary>
    Task<WoPayloadValidationResult> ValidateAndFilterAsync(
        RunContext context,
        JournalType journalType,
        string woPayloadJson,
        CancellationToken ct);
}
