namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain;

/// <summary>
/// Result of posting one journal group (typically per <see cref="JournalType"/>).
/// </summary>
public sealed record PostResult(
    JournalType JournalType,
    bool IsSuccess,
    string? JournalId,
    string? SuccessMessage,
    IReadOnlyList<PostError> Errors,
    int WorkOrdersBefore = 0,
    int WorkOrdersPosted = 0,
    int WorkOrdersFiltered = 0,
    string? ValidationResponseRaw = null,
    int RetryableWorkOrders = 0,
    int RetryableLines = 0,
    string? RetryablePayloadJson = null);

/// <summary>
/// Carries post error data.
/// </summary>
public sealed record PostError(
    string Code,
    string Message,
    string? StagingId,
    string? JournalId,
    bool JournalDeleted,
    string? DeleteMessage);
