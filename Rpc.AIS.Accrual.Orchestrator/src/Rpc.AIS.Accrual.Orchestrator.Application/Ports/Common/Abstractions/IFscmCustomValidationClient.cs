using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Calls the FSCM custom validation endpoint for an AIS Work Order payload.
/// This is a remote validation step that runs AFTER AIS local contract checks.
/// </summary>
public interface IFscmCustomValidationClient
{
    /// <summary>
    /// Validates the provided AIS payload JSON for the given journal type and returns any failures.
    /// Implementations must throw only for programmer errors; transport failures should be represented
    /// as failures with <see cref="ValidationDisposition.Retryable"/> or <see cref="ValidationDisposition.FailFast"/>
    /// depending on policy.
    /// </summary>
    Task<IReadOnlyList<WoPayloadValidationFailure>> ValidateAsync(
        RunContext context,
        JournalType journalType,
        string company,
        string woPayloadJson,
        CancellationToken ct);
}
