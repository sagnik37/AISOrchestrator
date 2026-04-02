using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Client responsible for posting validated records to FSCM journals.
/// </summary>
public interface IPostingClient
{
    /// <summary>
    /// Existing contract: post staging references grouped by journal type.
    /// </summary>
    Task<PostResult> PostAsync(
        RunContext context,
        JournalType journalType,
        IReadOnlyList<AccrualStagingRef> records,
        CancellationToken ct);

    /// <summary>
    /// New contract: post using the raw WO payload JSON (as received from FSA),
    /// validating the same payload and filtering failed work orders before posting.
    /// </summary>
    Task<PostResult> PostFromWoPayloadAsync(
        RunContext context,
        JournalType journalType,
        string woPayloadJson,
        CancellationToken ct);

    /// <summary>
    /// Post an already-validated WO payload batch (no validation call is performed in this method).
    ///
    /// Use this for large payload scenarios where:
    /// -  validate once (outside) and
    /// -  post the payload in smaller batches to avoid timeouts and throttling.
    ///
    /// The caller should pass any validation-related errors in <paramref name="preErrors"/> so they can be carried
    /// through to the final <see cref="PostResult"/>.
    /// </summary>
    Task<PostResult> PostValidatedWoPayloadAsync(
        RunContext context,
        JournalType journalType,
        string woPayloadJson,
        IReadOnlyList<PostError> preErrors,
        string? validationResponseRaw,
        CancellationToken ct);

    /// <summary>
    /// Validate the full WO payload ONCE (covers all journal types) and then post per journal type using the same
    /// validation response, filtering out only the failed journal type(s) per work order.
    ///
    /// This is the recommended path for WO payloads that may contain Item/Expense/Hour journals together.
    /// </summary>
    Task<List<PostResult>> ValidateOnceAndPostAllJournalTypesAsync(
        RunContext context,
        string woPayloadJson,
        CancellationToken ct);
}
