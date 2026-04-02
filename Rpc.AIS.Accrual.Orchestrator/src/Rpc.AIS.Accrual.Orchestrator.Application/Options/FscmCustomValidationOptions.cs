namespace Rpc.AIS.Accrual.Orchestrator.Core.Options;

/// <summary>
/// Options for calling the FSCM custom validation endpoint.
/// </summary>
public sealed class FscmCustomValidationOptions
{
    public const string SectionName = "Fscm:CustomValidation";

    /// <summary>
    /// Relative endpoint path under the FSCM base URL.
    /// Example: "/api/services/AIS/Validate" or "/api/services/RpcAis/Validate"
    /// </summary>
    public string EndpointPath { get; set; } = "/api/services/AIS/Validate";

    /// <summary>
    /// HTTP timeout in seconds for the validation call.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;
}
