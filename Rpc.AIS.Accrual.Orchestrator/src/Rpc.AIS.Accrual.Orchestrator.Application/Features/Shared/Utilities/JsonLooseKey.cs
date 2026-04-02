using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

/// <summary>
/// Shared loose-key JSON helpers (ignore spaces/underscores/hyphens; case-insensitive).
/// Extracted so both Core services and Infrastructure clients reuse identical behavior.
/// </summary>
public static class JsonLooseKey
{
    /// <summary>
    /// Executes normalize key loose.
    /// </summary>
    public static string NormalizeKeyLoose(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;

        Span<char> buf = stackalloc char[s.Length];
        var idx = 0;

        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch))
                buf[idx++] = char.ToLowerInvariant(ch);
        }

        return new string(buf[..idx]);
    }

    // ---------------- JsonObject (JsonNode) ----------------

    /// <summary>
    /// Executes try get node loose.
    /// </summary>
    public static bool TryGetNodeLoose(JsonObject obj, string key, out JsonNode? node)
    {
        node = null;

        // Fast path (exact)
        if (obj.TryGetPropertyValue(key, out var direct))
        {
            node = direct;
            return true;
        }

        // Case-insensitive match
        foreach (var kv in obj)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                node = kv.Value;
                return true;
            }
        }

        // Loose match (ignore spaces/underscores/hyphens, etc.)
        var target = NormalizeKeyLoose(key);
        if (target.Length == 0) return false;

        foreach (var kv in obj)
        {
            if (NormalizeKeyLoose(kv.Key) == target)
            {
                node = kv.Value;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Executes try get string loose.
    /// </summary>
    public static bool TryGetStringLoose(JsonObject obj, string key, out string? value)
    {
        value = null;

        if (!TryGetNodeLoose(obj, key, out var node) || node is null)
            return false;

        value = node.ToString();
        return true;
    }

    // ---------------- JsonElement (System.Text.Json) ----------------

    /// <summary>
    /// Executes try get property loose.
    /// </summary>
    public static bool TryGetPropertyLoose(JsonElement obj, string key, out JsonElement value)
    {
        value = default;

        if (obj.ValueKind != JsonValueKind.Object)
            return false;

        if (obj.TryGetProperty(key, out value))
            return true;

        var target = NormalizeKeyLoose(key);
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase) || NormalizeKeyLoose(p.Name) == target)
            {
                value = p.Value;
                return true;
            }
        }

        return false;
    }
}
