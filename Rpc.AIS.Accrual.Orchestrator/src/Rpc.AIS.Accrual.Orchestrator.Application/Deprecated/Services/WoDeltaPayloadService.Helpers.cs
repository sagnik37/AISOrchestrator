using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services;

public sealed partial class WoDeltaPayloadService
{

    private static IReadOnlyList<FscmJournalLine> FilterBySubProject(
       IReadOnlyList<FscmJournalLine> lines,
       string baselineSubProjectId)
    {
        if (lines.Count == 0) return lines;
        if (string.IsNullOrWhiteSpace(baselineSubProjectId)) return lines;

        var baseline = baselineSubProjectId.Trim();

        return lines
            .Where(l => !string.IsNullOrWhiteSpace(l.SubProjectId) &&
                        string.Equals(l.SubProjectId.Trim(), baseline, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string? TryGetString(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.String) return v.GetString();
        return v.ToString();
    }

    private static bool StringEquals(string? a, string? b)
        => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Mutates the parsed payload to make every line inactive (delta => full reversal).
    /// We stamp both 'statecode' (1) and 'IsActive' (false) to satisfy existing loose readers.
    /// For CancelToZero behavior, we also zero the quantity-bearing field so old-subproject
    /// customer change does not recreate positive item/expense/hour lines.
    /// </summary>
    private static void ForceAllLinesInactive(JsonNode root)
    {
        if (root is not JsonObject obj) return;
        if (!obj.TryGetPropertyValue(Keys.Request, out var reqNode) || reqNode is not JsonObject reqObj) return;
        if (!reqObj.TryGetPropertyValue(Keys.WoList, out var woListNode) || woListNode is not JsonArray woList) return;

        foreach (var woNode in woList.OfType<JsonObject>())
        {
            ForceInactiveForJournal(woNode, Keys.WoItemLines);
            ForceInactiveForJournal(woNode, Keys.WoExpLines);
            ForceInactiveForJournal(woNode, Keys.WoHourLines);
        }
    }

    private static void ForceInactiveForJournal(JsonObject woNode, string journalKey)
    {
        if (!woNode.TryGetPropertyValue(journalKey, out var jNode) || jNode is not JsonObject jObj) return;
        if (!jObj.TryGetPropertyValue(Keys.JournalLines, out var linesNode) || linesNode is not JsonArray arr) return;

        foreach (var ln in arr.OfType<JsonObject>())
        {
            ln["statecode"] = "1";
            ln["IsActive"] = "false";

            switch (journalKey)
            {
                case var k when string.Equals(k, Keys.WoItemLines, StringComparison.OrdinalIgnoreCase):
                case var k2 when string.Equals(k2, Keys.WoExpLines, StringComparison.OrdinalIgnoreCase):
                    SetNumericZeroLoose(ln, "Quantity");
                    break;

                case var k when string.Equals(k, Keys.WoHourLines, StringComparison.OrdinalIgnoreCase):
                    SetNumericZeroLoose(ln, "Duration");
                    break;
            }
        }
    }

    private static void SetNumericZeroLoose(JsonObject obj, string key)
    {
        if (obj is null || string.IsNullOrWhiteSpace(key)) return;

        if (JsonLooseKey.TryGetNodeLoose(obj, key, out var existing) && existing is not null)
        {
            obj[key] = CreateZeroLike(existing);
            return;
        }

        obj[key] = JsonValue.Create(0m);
    }

    private static JsonNode CreateZeroLike(JsonNode existing)
    {
        if (existing is JsonValue val)
        {
            if (val.TryGetValue<string>(out _))
                return JsonValue.Create("0");

            if (val.TryGetValue<int>(out _))
                return JsonValue.Create(0);

            if (val.TryGetValue<long>(out _))
                return JsonValue.Create(0L);

            if (val.TryGetValue<float>(out _))
                return JsonValue.Create(0f);

            if (val.TryGetValue<double>(out _))
                return JsonValue.Create(0d);

            if (val.TryGetValue<decimal>(out _))
                return JsonValue.Create(0m);

            if (val.TryGetValue<bool>(out _))
                return JsonValue.Create(false);
        }

        return JsonValue.Create(0m);
    }

    private static bool IsNullOrEmptyStringNode(JsonNode? node)
    {
        if (node is null) return true;
        var s = node.ToString();
        return string.IsNullOrWhiteSpace(s) || s == "\"\"" || s == "null";
    }

    /// <summary>
    /// Copies the first non-empty candidate from src into dst[dstKey].
    /// If dst already has dstKey but it's empty, it will be overwritten.
    /// </summary>
    private static void CopyFirstNonEmptyLoose(JsonObject src, JsonObject dst, string dstKey, params string[] candidates)
    {
        // If dst has a non-empty value already, keep it.
        if (JsonLooseKey.TryGetNodeLoose(dst, dstKey, out var existing) && !IsNullOrEmptyStringNode(existing))
            return;

        foreach (var c in candidates)
        {
            if (!JsonLooseKey.TryGetNodeLoose(src, c, out var n) || IsNullOrEmptyStringNode(n))
                continue;

            dst[dstKey] = n!.DeepClone();
            return;
        }
    }

    private static void EnsureStringAlways(JsonObject obj, string key)
    {
        if (obj is null) return;

        if (JsonLooseKey.TryGetNodeLoose(obj, key, out var existing) && existing is not null)
        {
            // Normalize null/empty to empty string.
            if (IsNullOrEmptyStringNode(existing))
                obj[key] = "";
            return;
        }

        obj[key] = "";
    }

    private static void EnsureNumberAlways(JsonObject obj, string key, decimal defaultValue)
    {
        if (obj is null) return;

        if (JsonLooseKey.TryGetNodeLoose(obj, key, out var existing) && existing is not null)
        {
            // If the value is null/blank, force default number.
            if (IsNullOrEmptyStringNode(existing))
                obj[key] = JsonValue.Create(defaultValue);
            return;
        }

        obj[key] = JsonValue.Create(defaultValue);
    }

    /// <summary>
    /// Derive WO-level DimensionDisplayValue from the first available journal line, if header is missing.
    /// </summary>
    private static string? TryDeriveHeaderDimensionDisplayValue(JsonObject woObj)
    {
        static string? FromSection(JsonObject wo, params string[] sectionKeys)
        {
            foreach (var sk in sectionKeys)
            {
                if (!JsonLooseKey.TryGetNodeLoose(wo, sk, out var secNode) || secNode is not JsonObject secObj)
                    continue;

                if (!JsonLooseKey.TryGetNodeLoose(secObj, "JournalLines", out var linesNode) || linesNode is not JsonArray lines || lines.Count == 0)
                    continue;

                foreach (var ln in lines)
                {
                    if (ln is not JsonObject lineObj) continue;
                    if (JsonLooseKey.TryGetStringLoose(lineObj, "DimensionDisplayValue", out var ddv) && !string.IsNullOrWhiteSpace(ddv))
                        return ddv;
                }
            }
            return null;
        }

        return
            FromSection(woObj, "WOItemLines", "WO Item Lines") ??
            FromSection(woObj, "WOExpLines", "WO Expense Lines") ??
            FromSection(woObj, "WOHourLines", "WO Hour Lines");
    }

}
