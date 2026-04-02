using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Decorator that caches ItemNumber -> ReleasedDistinctProductCategory mappings to avoid repeated FSCM OData calls.
/// Drop-in replacement for IFscmReleasedDistinctProductsClient.
/// </summary>
public sealed class CachedFscmReleasedDistinctProductsClient : IFscmReleasedDistinctProductsClient
{
    private readonly IFscmReleasedDistinctProductsClient _inner;
    private readonly IMemoryCache _cache;
    private readonly FscmReleasedDistinctProductsCacheOptions _opt;
    private readonly ILogger<CachedFscmReleasedDistinctProductsClient> _logger;

    // Prevents stampede: many parallel runs fetching same missing set.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CachedFscmReleasedDistinctProductsClient(
        IFscmReleasedDistinctProductsClient inner,
        IMemoryCache cache,
        IOptions<FscmReleasedDistinctProductsCacheOptions> options,
        ILogger<CachedFscmReleasedDistinctProductsClient> logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _opt = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyDictionary<string, ReleasedDistinctProductCategory>> GetCategoriesByItemNumberAsync(
        RunContext ctx,
        IReadOnlyList<string> itemNumbers,
        CancellationToken ct)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        if (itemNumbers is null) throw new ArgumentNullException(nameof(itemNumbers));

        if (!_opt.Enabled)
            return await _inner.GetCategoriesByItemNumberAsync(ctx, itemNumbers, ct).ConfigureAwait(false);

        var clean = itemNumbers
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (clean.Count == 0)
            return new Dictionary<string, ReleasedDistinctProductCategory>(StringComparer.OrdinalIgnoreCase);

        if (clean.Count > _opt.MaxItemCountPerCall)
            throw new InvalidOperationException($"ItemNumbers count exceeds limit ({clean.Count} > {_opt.MaxItemCountPerCall}).");

        var results = new Dictionary<string, ReleasedDistinctProductCategory>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>(capacity: Math.Min(clean.Count, 2048));
        var negativeHits = 0;

        // 1) Cache read (no lock)
        foreach (var item in clean)
        {
            if (_cache.TryGetValue(CacheKey(item), out CacheEntry? entry) && entry is not null)
            {
                if (entry.IsFound && entry.Value is not null)
                    results[item] = entry.Value;
                else
                    negativeHits++;
            }
            else
            {
                missing.Add(item);
            }
        }

        if (missing.Count == 0)
        {
            _logger.LogInformation(
                "ReleasedDistinctProducts cache HIT. Items={Total} Hits={Hits} NegativeHits={NegHits} RunId={RunId} CorrelationId={CorrelationId}",
                clean.Count, results.Count, negativeHits, ctx.RunId, ctx.CorrelationId);

            return results;
        }

        // 2) Gate to avoid stampede (re-check inside)
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var stillMissing = new List<string>(missing.Count);

            foreach (var item in missing)
            {
                if (_cache.TryGetValue(CacheKey(item), out CacheEntry? entry) && entry is not null)
                {
                    if (entry.IsFound && entry.Value is not null)
                        results[item] = entry.Value;
                    else
                        negativeHits++;
                }
                else
                {
                    stillMissing.Add(item);
                }
            }

            if (stillMissing.Count == 0)
            {
                _logger.LogInformation(
                    "ReleasedDistinctProducts cache post-gate HIT. Items={Total} Hits={Hits} NegativeHits={NegHits} RunId={RunId} CorrelationId={CorrelationId}",
                    clean.Count, results.Count, negativeHits, ctx.RunId, ctx.CorrelationId);

                return results;
            }

            _logger.LogInformation(
                "ReleasedDistinctProducts cache MISS => calling FSCM. Total={Total} CachedHits={Hits} ToFetch={Missing} RunId={RunId} CorrelationId={CorrelationId}",
                clean.Count, results.Count, stillMissing.Count, ctx.RunId, ctx.CorrelationId);

            // 3) Fetch only missing
            var fetched = await _inner.GetCategoriesByItemNumberAsync(ctx, stillMissing, ct).ConfigureAwait(false);

            // 4) Cache positives
            foreach (var kvp in fetched)
            {
                var item = kvp.Key?.Trim();
                if (string.IsNullOrWhiteSpace(item))
                    continue;

                var val = kvp.Value;

                _cache.Set(
                    CacheKey(item),
                    CacheEntry.FoundValue(val),
                    new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = _opt.Ttl });

                results[item] = val;
            }

            // 5) Negative cache for missing not returned
            var fetchedKeys = new HashSet<string>(fetched.Keys, StringComparer.OrdinalIgnoreCase);
            var negCount = 0;

            foreach (var item in stillMissing)
            {
                if (fetchedKeys.Contains(item))
                    continue;

                _cache.Set(
                    CacheKey(item),
                    CacheEntry.NotFoundValue(),
                    new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = _opt.NegativeTtl });

                negCount++;
            }

            _logger.LogInformation(
                "ReleasedDistinctProducts cache updated. Total={Total} Hits={Hits} NewlyFetched={Fetched} NewlyNegCached={Neg} RunId={RunId} CorrelationId={CorrelationId}",
                clean.Count, results.Count, fetched.Count, negCount, ctx.RunId, ctx.CorrelationId);

            return results;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string CacheKey(string itemNumber)
        => "FSCM:RDP:" + (itemNumber ?? string.Empty).Trim().ToUpperInvariant();

    private sealed record CacheEntry(bool IsFound, ReleasedDistinctProductCategory? Value)
    {
        public static CacheEntry FoundValue(ReleasedDistinctProductCategory value) => new(true, value);
        public static CacheEntry NotFoundValue() => new(false, null);
    }
}
