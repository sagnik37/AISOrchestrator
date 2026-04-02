namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain;

/// <summary>
/// Run summary for the accrual orchestration.
///
/// WO-payload mode:
/// - WorkOrdersConsidered / WorkOrdersValid / WorkOrdersInvalid are the primary counters.
/// </summary>
public sealed record RunOutcome(
    RunContext Context,
    int StagingRecordsConsidered,
    int ValidRecords,
    int InvalidRecords,
    IReadOnlyList<PostResult> PostResults,
    IReadOnlyList<ValidationResult> ValidationFailures,
    IReadOnlyList<string> GeneralErrors,
    int WorkOrdersConsidered = 0,
    int WorkOrdersValid = 0,
    int WorkOrdersInvalid = 0)
{
    public bool HasAnyErrors =>
        // Legacy invalid count
        InvalidRecords > 0
        // WO-payload invalid count
        || WorkOrdersInvalid > 0
        // Any posting failures
        || PostResults.Any(r => !r.IsSuccess)
        // Any run-level errors
        || GeneralErrors.Count > 0;
}
