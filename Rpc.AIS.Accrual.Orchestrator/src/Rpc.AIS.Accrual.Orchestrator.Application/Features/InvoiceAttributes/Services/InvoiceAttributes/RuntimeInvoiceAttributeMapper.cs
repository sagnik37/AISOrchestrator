using System;
using System.Collections.Generic;
using System.Linq;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.InvoiceAttributes;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.InvoiceAttributes;

/// <summary>
/// Generates runtime mapping between FS attribute keys and FSCM attribute names.
/// Mapping is generated during execution using FSCM definitions ("attribute table") stored in memory.
/// </summary>
public sealed class RuntimeInvoiceAttributeMapper
{
    public sealed record MappingResult(
        IReadOnlyDictionary<string, string> FsKeyToFscmName,
        IReadOnlyList<string> UnmappedFsKeys);

    public static MappingResult BuildMapping(
        IReadOnlyDictionary<string, string?> fsAttributes,
        IReadOnlyList<InvoiceAttributeDefinition> fscmDefinitions)
    {
        fsAttributes ??= new Dictionary<string, string?>();
        fscmDefinitions ??= Array.Empty<InvoiceAttributeDefinition>();

        // Active attribute names from FSCM (case-insensitive)
        var fscmNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in fscmDefinitions.Where(d => d is not null && d.Active))
        {
            if (string.IsNullOrWhiteSpace(d.AttributeName)) continue;
            fscmNames[d.AttributeName] = d.AttributeName;
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var unmapped = new List<string>();

        foreach (var fsKey in fsAttributes.Keys)
        {
            if (string.IsNullOrWhiteSpace(fsKey)) continue;

            // Direct match (preferred)
            if (fscmNames.TryGetValue(fsKey, out var direct))
            {
                map[fsKey] = direct;
                continue;
            }

            // Normalized match
            var norm = NormalizeKey(fsKey);
            var matched = fscmNames.Keys.FirstOrDefault(k => NormalizeKey(k) == norm);
            if (!string.IsNullOrWhiteSpace(matched))
            {
                map[fsKey] = fscmNames[matched];
                continue;
            }

            unmapped.Add(fsKey);
        }

        return new MappingResult(map, unmapped);
    }

    internal static string NormalizeKey(string s)
    {
        var chars = s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant);
        return new string(chars.ToArray());
    }
}
