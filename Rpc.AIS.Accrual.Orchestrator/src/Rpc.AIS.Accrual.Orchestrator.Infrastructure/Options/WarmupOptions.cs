namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

/// <summary>
/// Lightweight warm-up endpoint configuration.
/// This is intentionally "read-only" and "small payload" to prime auth/TLS/DNS/HttpClient pipelines
/// without performing expensive paging scans.
/// </summary>
public sealed class WarmupOptions
{
    public const string SectionName = "Warmup";

    /// <summary>Feature flag. When false, the warm-up endpoint returns 404.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Hard safety clamp to prevent long-running warm-up calls.</summary>
    public int MaxMinutes { get; set; } = 10;

    /// <summary>Default minutes if request omits it.</summary>
    public int DefaultMinutes { get; set; } = 3;

    /// <summary>Default overall max concurrency if request omits it.</summary>
    public int DefaultMaxConcurrency { get; set; } = 2;

    /// <summary>Default delay between loops (milliseconds) if request omits it.</summary>
    public int DefaultDelayMs { get; set; } = 800;

    /// <summary>
    /// Relative Dataverse Web API paths (relative to FsOptions.DataverseApiBaseUrl) to GET each loop.
    /// Examples: "WhoAmI", "systemusers?$select=systemuserid&$top=1"
    /// </summary>
    public string[] DataversePaths { get; set; } = new[]
    {
        "WhoAmI",
        "systemusers?$select=systemuserid&$top=1"
    };

    /// <summary>
    /// Relative FSCM paths (relative to FscmOptions.BaseUrl) to GET each loop.
    /// Examples: "data/Companies?$select=DataArea&$top=1"
    /// Note: FSCM entity sets vary by environment; keep these configurable.
    /// </summary>
    public string[] FscmPaths { get; set; } = new[]
    {
        "data/Companies?$top=1"
    };
}
