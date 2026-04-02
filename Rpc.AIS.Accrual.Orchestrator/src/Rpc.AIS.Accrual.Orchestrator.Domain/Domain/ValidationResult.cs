namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain;

/// <summary>
/// Carries validation result data.
/// </summary>
public sealed record ValidationResult(
    AccrualStagingRef Record,
    bool IsValid,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static ValidationResult Valid(AccrualStagingRef record) =>
        new(record, true, null, null);

    public static ValidationResult Invalid(AccrualStagingRef record, string errorCode, string errorMessage) =>
        new(record, false, errorCode, errorMessage);
}
