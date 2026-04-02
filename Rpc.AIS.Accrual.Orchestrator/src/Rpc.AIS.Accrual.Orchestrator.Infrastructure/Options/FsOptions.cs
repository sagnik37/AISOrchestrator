namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

/// <summary>
/// Unified options for Field Service / Dataverse side of AIS.
/// Combines previously separate options:
/// - FsaIngestionOptions (Core) for ingestion/paging/filter
/// - DataverseAuthOptions for AAD client credentials
/// - EndpointsOptions (FSA section) for durable polling + function key
/// </summary>
public sealed class FsOptions
{
    public const string SectionName = "Fs";

    // -------------------------
    // Dataverse API (OData)
    // -------------------------
    /// <summary>Dataverse Web API base URL, e.g. https://{org}.crm.dynamics.com/api/data/v9.2</summary>
    public string DataverseApiBaseUrl { get; set; } = "";

    /// <summary>OData filter for open work orders.</summary>
    public string? WorkOrderFilter { get; set; }

    /// <summary>Prefer delta payload build before posting (default false).</summary>
    public bool ApplyFscmDeltaBeforePosting { get; set; } = false;

    public int PageSize { get; set; } = 500;
    public int MaxPages { get; set; } = 20;

    /// <summary>
    /// Dataverse server-side max page size hint via Prefer header: odata.maxpagesize={n}.
    /// If not configured, defaults to 5000.
    /// </summary>
    public int PreferMaxPageSize { get; set; } = 5000;

    /// <summary>
    /// OR-filter batching chunk size for Dataverse queries that aggregate by Work Order IDs.
    /// If not configured, defaults to 25.
    /// </summary>
    public int OrFilterChunkSize { get; set; } = 25;

    // -------------------------
    // Dataverse AAD app (client credentials)
    // -------------------------
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    // -------------------------
    // FSA Transactions Function (optional helper endpoint)
    // -------------------------
    public string? FsaBaseUrl { get; set; }
    public string? FsaPath { get; set; }
    public string? FsaFunctionKey { get; set; }

    public int FsaDurablePollSeconds { get; set; } = 2;
    public int FsaDurableTimeoutSeconds { get; set; } = 600;

    // -------------------------
    // Phase-3 toggles (backward compatible)
    // -------------------------
    /// <summary>
    /// If true, AIS will only process work orders that have a sub-project lookup present.
    /// If false (default), behavior is unchanged.
    /// </summary>
    public bool RequireSubProjectForProcessing { get; set; } = false;

    /// <summary>
    /// If true, virtual lookup name resolution will be skipped (no extra Dataverse calls).
    /// If false (default), behavior is unchanged.
    /// </summary>
    public bool DisableVirtualLookupResolution { get; set; } = false;


    /// <summary>
    /// If true, internal attachments are also synced to FSCM in addition to external attachments.
    /// If false (default), only external attachments are synced.
    /// </summary>
    public bool SyncInternalDocumentsToFscm { get; set; } = false;

}
