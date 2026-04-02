using System;
using System.Text.Json;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload;

/// <summary>
/// Small JSON helper utilities used by the FSA delta payload full-fetch path.
/// Kept in one place to avoid duplicating fragile JSON parsing logic.
/// </summary>
internal static class FsaDeltaPayloadJsonUtil
{
    internal static bool TryGuid(JsonElement row, string propertyName, out Guid id)
    {
        id = default;

        if (row.ValueKind != JsonValueKind.Object) return false;
        if (!row.TryGetProperty(propertyName, out var v)) return false;

        if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            return Guid.TryParse(s, out id);
        }

        // Dataverse sometimes returns GUIDs as raw JSON string or null.
        return false;
    }

    internal static bool LooksLikeGuid(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        return Guid.TryParse(s.Trim(), out _);
    }

    /// <summary>
    /// Tries to read only the formatted value for a field (Dataverse OData formatted value annotation).
    /// </summary>
    internal static bool TryFormattedOnly(JsonElement row, string logicalFieldName, out string? formatted)
    {
        formatted = null;
        if (row.ValueKind != JsonValueKind.Object) return false;

        var formattedName = logicalFieldName + "@OData.Community.Display.V1.FormattedValue";
        if (row.TryGetProperty(formattedName, out var fv) && fv.ValueKind == JsonValueKind.String)
        {
            formatted = fv.GetString();
            return !string.IsNullOrWhiteSpace(formatted);
        }

        return false;
    }

    /// <summary>
    /// Parse of a GUID that might be wrapped like "{guid}" or contain other noise.
    /// </summary>
    internal static Guid? ParseGuidLoose(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var s = raw.Trim();

        // common wrappers
        s = s.Trim('{', '}', '(', ')');

        if (Guid.TryParse(s, out var g)) return g;

        // fall back: extract a GUID-looking token
        // (avoid regex dependency; simple scan)
        for (var i = 0; i <= s.Length - 36; i++)
        {
            var sub = s.Substring(i, 36);
            if (Guid.TryParse(sub, out g)) return g;
        }

        return null;
    }
}
