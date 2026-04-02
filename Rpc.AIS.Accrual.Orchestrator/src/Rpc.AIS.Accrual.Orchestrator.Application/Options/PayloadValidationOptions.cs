namespace Rpc.AIS.Accrual.Orchestrator.Core.Options;

/// <summary>
/// Controls AIS-side payload validation behavior.
/// </summary>
public sealed class PayloadValidationOptions
{
    public const string SectionName = "Validation";

    /// <summary>
    /// If true (default), any invalid journal line causes the entire Work Order
    /// to be excluded from the postable payload for that journal type.
    /// </summary>
    public bool DropWholeWorkOrderOnAnyInvalidLine { get; set; } = true;
    /// <summary>
    /// Enables calling the FSCM custom validation endpoint AFTER local AIS validation.
    /// This should be enabled when FSCM provides a dedicated validation API to validate the AIS payload contract.
    /// </summary>
    public bool EnableFscmCustomEndpointValidation { get; set; }

    /// <summary>
    /// If true (default), failures calling the FSCM custom validation endpoint will fail-closed:
    /// AIS will stop the run (FailFast) rather than posting potentially invalid data.
    /// If false, remote validation call failures will be treated as retryable for the affected work orders.
    /// </summary>
    public bool FailClosedOnFscmCustomValidationError { get; set; } = true;

    /// <summary>
    /// Maximum attempts for retryable validation failures (per journal type) within one orchestration run.
    /// </summary>
    public int RetryMaxAttempts { get; set; } = 3;

    /// <summary>
    /// Retry delays in minutes for attempts 1..N (1-based). If there are fewer entries than attempts,
    /// the last delay value is reused.
    /// </summary>
    public int[] RetryDelaysMinutes { get; set; } = new[] { 5, 15, 30 };
}
