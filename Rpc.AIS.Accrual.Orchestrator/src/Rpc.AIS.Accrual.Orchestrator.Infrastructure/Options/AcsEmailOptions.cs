namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

/// <summary>
/// Azure Communication Services Email configuration.
/// Use Key Vault references for ConnectionString in Azure Function App settings.
/// </summary>
public sealed class AcsEmailOptions
{
    public const string SectionName = "AcsEmail";

    /// <summary>
    /// If false, email sending is disabled and NoopEmailSender should be used.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// ACS Email connection string (store in Key Vault; load via App Settings / KeyVault reference).
    /// Example setting name: "AcsEmail:ConnectionString"
    /// </summary>
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>
    /// Sender email address. MUST be a verified sender/domain in ACS Email.
    /// Example: "no-reply@domain.com"
    /// </summary>
    public string FromAddress { get; init; } = string.Empty;

    /// <summary>
    /// Optional display name.
    /// </summary>
    public string FromDisplayName { get; init; } = string.Empty;

    /// <summary>
    /// If true, waits until ACS completes processing. If false, returns once accepted/started.
    /// Recommended: false for functions to reduce execution time.
    /// </summary>
    public bool WaitUntilCompleted { get; init; } = false;
}
