namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

/// <summary>
/// Configuration for FSCM OData staging (Option B):
/// - Header entity POST
/// - Lines via /data/OData changeset
///
/// This is intentionally separated from existing posting options so existing deployments
/// remain unchanged unless this feature is explicitly enabled.
/// </summary>
public sealed class FscmODataStagingOptions
{
    public const string SectionName = "Fscm:ODataStaging";

    /// <summary>Master feature toggle. Default false.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Base FSCM host URL, e.g. https://&lt;env&gt;.operations.dynamics.com
    /// If not provided, the system will fall back to Endpoints:FscmBaseUrl.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Maximum number of lines per OData changeset.</summary>

    /// <summary>
    /// Maximum concurrent journal staging operations per legal entity (dataAreaId).
    /// This is an AIS-side throttle to reduce 429s.
    /// </summary>
    public int MaxConcurrentJournalsPerLegalEntity { get; set; } = 2;

    /// <summary>Voucher prefix used when deterministic vouchers are required (Project item).</summary>
    public string VoucherPrefix { get; set; } = "AIS";

    /// <summary>Header idempotency key prefix stored in header Txt/Text/Description.</summary>
    public string HeaderIdempotencyPrefix { get; set; } = "AIS";
}
