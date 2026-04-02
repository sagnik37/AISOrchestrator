// File: .../Core/Services/WoDeltaPayload/WoDeltaPayloadClassifier.cs
// Extracted from WoDeltaPayloadService.Helpers.cs to improve SRP (classification + tolerant reads).

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

internal static class WoDeltaPayloadClassifier
{
    internal static List<JsonNode> GetWoList(JsonNode root)
    {
        // tolerate casing/spacing differences
        if (root is not JsonObject obj) return new List<JsonNode>(0);

        var req = FindFirstObjectLoose(obj, "_request", "_Request", "request");
        var listNode = req is null ? null : FindFirstNodeLoose(req, "WOList", "woList", "Wolist");

        if (listNode is not JsonArray arr)
            return new List<JsonNode>(0);

        var result = new List<JsonNode>(arr.Count);
        foreach (var n in arr)
        {
            if (n is not null) result.Add(n);
        }
        return result;
    }

    internal static Guid GetWorkOrderGuid(JsonObject woObj)
    {
        if (TryGetStringLoose(woObj, "WorkOrderGUID", out var s) ||
            TryGetStringLoose(woObj, "Work order GUID", out s) ||
            TryGetStringLoose(woObj, "WorkOrderGuid", out s))
        {
            s = NormalizeGuidString(s);
            return Guid.TryParse(s, out var g) ? g : Guid.Empty;
        }

        return Guid.Empty;
    }

    internal static string? GetStringLoose(JsonObject obj, string key)
    {
        if (!TryGetNodeLoose(obj, key, out var n) || n is null) return null;
        var s = n.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    // ------------------
    // Loose JSON helpers
    // ------------------

    private static JsonObject? FindFirstObjectLoose(JsonObject obj, params string[] candidates)
    {
        foreach (var c in candidates)
        {
            if (TryGetNodeLoose(obj, c, out var n) && n is JsonObject o)
                return o;
        }
        return null;
    }

    private static JsonNode? FindFirstNodeLoose(JsonObject obj, params string[] candidates)
    {
        foreach (var c in candidates)
        {
            if (TryGetNodeLoose(obj, c, out var n) && n is not null)
                return n;
        }
        return null;
    }

    private static bool TryGetNodeLoose(JsonObject obj, string key, out JsonNode? node)
    {
        // Exact first
        if (obj.TryGetPropertyValue(key, out node))
            return true;

        var nk = NormalizeKey(key);
        foreach (var kv in obj)
        {
            if (NormalizeKey(kv.Key) == nk)
            {
                node = kv.Value;
                return true;
            }
        }

        node = null;
        return false;
    }

    private static bool TryGetStringLoose(JsonObject obj, string key, out string? value)
    {
        value = null;
        if (!TryGetNodeLoose(obj, key, out var n) || n is null) return false;
        var s = n.ToString();
        if (string.IsNullOrWhiteSpace(s)) return false;
        value = s;
        return true;
    }

    private static string NormalizeKey(string s)
        => (s ?? string.Empty)
            .Trim()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    private static string? NormalizeGuidString(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        return s.Trim().Trim('{', '}');
    }
}
