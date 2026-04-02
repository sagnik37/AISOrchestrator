using System;
using System.Collections.Generic;
using System.Linq;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain.InvoiceAttributes;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.InvoiceAttributes;

/// <summary>
/// Compares FS attribute values against FSCM current values (both in memory) and returns delta updates.
/// FS is always system-of-record: if values differ, FS value is sent to FSCM.
///
/// NOTE (case-insensitive schema matching):
/// - FS may send schema keys like "rpc_wellname"
/// - FSCM FieldFXSchemaName may be "rpc_Wellname"
/// This builder normalizes both sides when mapping keys and when reading the FSCM snapshot,
/// so camel-case differences do not prevent updates.
/// </summary>
public sealed class InvoiceAttributeDeltaBuilder
{
    public sealed record DeltaResult(
        IReadOnlyList<InvoiceAttributePair> Updates,
        int MappedCount,
        int ChangedCount,
        int UnchangedCount,
        int MissingInFscmSnapshotCount);

    public static DeltaResult BuildDelta(
        IReadOnlyDictionary<string, string?> fsAttributes,
        IReadOnlyDictionary<string, string> fsKeyToFscmName,
        IReadOnlyDictionary<string, string?> fscmCurrentByName)
    {
        fsAttributes ??= new Dictionary<string, string?>();
        fsKeyToFscmName ??= new Dictionary<string, string>();
        fscmCurrentByName ??= new Dictionary<string, string?>();

        // Build case-insensitive view of FS attributes so "rpc_wellname" and "rpc_WellName" are equivalent.
        // If duplicates exist by casing, prefer the first non-empty value; otherwise keep first seen.
        var fsByKeyCI = BuildCaseInsensitiveValueMap(fsAttributes);

        // Build case-insensitive view of FSCM snapshot by schema name.
        // IMPORTANT: preserve the canonical (original) schema name from FSCM for outbound updates.
        var fscmSnapshotCI = BuildCaseInsensitiveSnapshotMap(fscmCurrentByName);

        var updates = new List<InvoiceAttributePair>();
        var mapped = 0;
        var changed = 0;
        var unchanged = 0;
        var missingSnapshot = 0;

        foreach (var kvp in fsKeyToFscmName)
        {
            var fsKey = kvp.Key;
            var mappedFscmName = kvp.Value; // configured target schema name
            mapped++;

            // FS value lookup: case-insensitive by FS key
            fsByKeyCI.TryGetValue(NormKey(fsKey), out var fsRaw);
            var fsVal = NormalizeValue(fsRaw);

            // FSCM snapshot lookup: case-insensitive by FSCM schema name
            var normFscmName = NormKey(mappedFscmName);
            var hasSnapshot = fscmSnapshotCI.TryGetValue(normFscmName, out var snap);
            if (!hasSnapshot)
            {
                missingSnapshot++;
            }

            var fscmVal = NormalizeValue(hasSnapshot ? snap.Value : null);

            if (!StringEquals(fsVal, fscmVal))
            {
                // Use canonical FSCM schema name when available; else fall back to mapped schema name
                var outName = hasSnapshot ? snap.CanonicalName : mappedFscmName;
                updates.Add(new InvoiceAttributePair(outName, fsVal));
                changed++;
            }
            else
            {
                unchanged++;
            }
        }

        return new DeltaResult(updates, mapped, changed, unchanged, missingSnapshot);
    }

    private static Dictionary<string, string?> BuildCaseInsensitiveValueMap(IReadOnlyDictionary<string, string?> source)
    {
        // Key: normalized (upper) schema key
        // Value: original value (raw), with preference for non-empty when duplicates by casing exist
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var kvp in source)
        {
            var key = NormKey(kvp.Key);
            if (string.IsNullOrEmpty(key))
                continue;

            if (!map.TryGetValue(key, out var existing))
            {
                map[key] = kvp.Value;
                continue;
            }

            // Prefer first non-empty value if we previously stored empty/null
            var existingNorm = NormalizeValue(existing);
            var incomingNorm = NormalizeValue(kvp.Value);

            if (existingNorm is null && incomingNorm is not null)
            {
                map[key] = kvp.Value;
            }
        }

        return map;
    }

    private readonly record struct SnapshotEntry(string CanonicalName, string? Value);

    private static Dictionary<string, SnapshotEntry> BuildCaseInsensitiveSnapshotMap(IReadOnlyDictionary<string, string?> snapshotByName)
    {
        // Key: normalized (upper) schema name
        // Value: canonical schema name + value
        var map = new Dictionary<string, SnapshotEntry>(StringComparer.Ordinal);

        foreach (var kvp in snapshotByName)
        {
            var canonical = kvp.Key;
            var key = NormKey(canonical);
            if (string.IsNullOrEmpty(key))
                continue;

            // Prefer first seen canonical name; for value, prefer non-empty if duplicates exist.
            if (!map.TryGetValue(key, out var existing))
            {
                map[key] = new SnapshotEntry(canonical, kvp.Value);
                continue;
            }

            var existingNorm = NormalizeValue(existing.Value);
            var incomingNorm = NormalizeValue(kvp.Value);

            if (existingNorm is null && incomingNorm is not null)
            {
                map[key] = new SnapshotEntry(existing.CanonicalName, kvp.Value);
            }
        }

        return map;
    }

    private static string NormKey(string? key)
        => string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim().ToUpperInvariant();

    private static string? NormalizeValue(string? v)
        => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static bool StringEquals(string? a, string? b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
