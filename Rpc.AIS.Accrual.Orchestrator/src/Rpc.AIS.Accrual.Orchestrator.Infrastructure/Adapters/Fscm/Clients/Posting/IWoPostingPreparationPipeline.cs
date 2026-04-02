using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;

/// <summary>
/// Prepares a WO payload for posting a single journal type:
/// normalize -> shape guard -> local validation+filter -> optional remote validation -> project section.
/// </summary>
public interface IWoPostingPreparationPipeline
{
    Task<PreparedWoPosting> PrepareAsync(RunContext ctx, JournalType journalType, string woPayloadJson, CancellationToken ct);

    /// <summary>
    /// Prepares an already-validated WO payload (skips local/remote validation) and projects it for a single journal type.
    /// </summary>
    Task<PreparedWoPosting> PrepareValidatedAsync(RunContext ctx, JournalType journalType, string woPayloadJson, string? validationResponseRaw, CancellationToken ct);
}

/// <summary>
/// Prepared payload and metadata for posting.
/// </summary>
public sealed record PreparedWoPosting(
    JournalType JournalType,
    string NormalizedPayloadJson,
    string ProjectedJournalPayloadJson,
    int WorkOrdersBefore,
    int WorkOrdersAfter,
    int RemovedDueToMissingOrEmptySection,
    System.Collections.Generic.List<Rpc.AIS.Accrual.Orchestrator.Core.Domain.PostError> PreErrors,
    string? ValidationResponseRaw,
    int RetryableWorkOrders,
    int RetryableLines,
    string? RetryablePayloadJson);
