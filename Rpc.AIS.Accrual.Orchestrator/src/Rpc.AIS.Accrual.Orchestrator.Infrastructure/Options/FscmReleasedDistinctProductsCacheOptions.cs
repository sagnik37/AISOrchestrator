using System;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

/// <summary>
/// Cache policy for FSCM CDSReleasedDistinctProducts (ItemNumber -> CategoryId mapping).
/// This cache is intended to dramatically reduce repeated OData calls across runs on the same warm host.
/// </summary>
public sealed class FscmReleasedDistinctProductsCacheOptions
{
    public const string SectionName = "Fscm:ReleasedDistinctProductsCache";

    /// <summary>
    /// Enables caching decorator.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// TTL for positive (found) entries. Default 24 hours.
    /// </summary>
    public TimeSpan Ttl { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    /// TTL for negative (not found) entries. Default 60 minutes.
    /// Prevents repeated calls for ItemNumbers that FSCM does not return.
    /// </summary>
    public TimeSpan NegativeTtl { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Upper bound on the number of ItemNumbers accepted per call.
    /// Helps prevent accidental huge allocations. Default 200k.
    /// </summary>
    public int MaxItemCountPerCall { get; init; } = 200_000;
}
