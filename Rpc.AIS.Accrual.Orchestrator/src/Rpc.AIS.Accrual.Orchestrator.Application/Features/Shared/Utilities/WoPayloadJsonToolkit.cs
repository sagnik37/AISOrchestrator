using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

/// <summary>
/// Shared JSON helper routines for the Work Order payload contract.
/// Placed in Core so both infrastructure clients and core services can reuse identical behavior.
/// </summary>
public static class WoPayloadJsonToolkit
{
    public const string RequestKey = "_request";
    public const string WoListKey = "WOList";
    public const string JournalLinesKey = "JournalLines";

    private static readonly JsonSerializerOptions CompactJson = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // Treat explicit null journal sections as absent by removing those properties during normalization.
    // This prevents hard-fail schema guards that require a section (if present) to be an object.
    private static void RemoveNullSectionsFromWoList(JsonObject req)
    {
        if (req is null) return;

        if (req[WoListKey] is not JsonArray woList)
            return;

        // Canonical section keys used throughout AIS.
        // Remove if value is null; keep if object/array.
        var sectionKeys = new[] { "WOExpLines", "WOHourLines", "WOItemLines" };

        foreach (var node in woList)
        {
            if (node is not JsonObject wo)
                continue;

            foreach (var sectionKey in sectionKeys)
            {
                RemoveNullPropertyLoose(wo, sectionKey);
            }
        }
    }

    private static void RemoveNullPropertyLoose(JsonObject obj, string targetKey)
    {
        // Fast-path: exact key match.
        if (obj.TryGetPropertyValue(targetKey, out var exact))
        {
            if (exact is null)
                obj.Remove(targetKey);
            return;
        }

        // Loose key match (ignore spaces/underscores/hyphens etc.)
        var target = JsonLooseKey.NormalizeKeyLoose(targetKey);
        foreach (var kv in obj.ToList())
        {
            if (JsonLooseKey.NormalizeKeyLoose(kv.Key) == target)
            {
                if (kv.Value is null)
                    obj.Remove(kv.Key);
                return;
            }
        }
    }

    /// <summary>
    /// Normalizes the WO payload shape so that the root contains "_request" and "_request.WOList".
    /// This tolerates common contract variants (e.g., "request" vs "_request", "woList" vs "WOList").
    /// </summary>
    public static string NormalizeWoPayloadToWoListKey(string woPayloadJson)
    {
        if (string.IsNullOrWhiteSpace(woPayloadJson)) return woPayloadJson;

        var rootNode = JsonNode.Parse(woPayloadJson);
        if (rootNode is not JsonObject root)
            return woPayloadJson;

        // Normalize: "request" -> "_request"
        if (root[RequestKey] is not JsonObject req)
        {
            if (root["request"] is JsonObject req2)
            {
                // : clone, because req2 already has a parent
                var cloned = (JsonObject)req2.DeepClone();
                root[RequestKey] = cloned;
                root.Remove("request");
                req = cloned;
            }
            else
            {
                return woPayloadJson;
            }
        }

        // Normalize: WO list key variants -> "WOList"
        // Even if already present, we still run section cleanup (null sections should be treated as absent).
        if (req[WoListKey] is not null)
        {
            RemoveNullSectionsFromWoList(req);
            return root.ToJsonString(CompactJson);
        }

        if (req["wo list"] is JsonArray legacyWoList)
        {
            req[WoListKey] = legacyWoList.DeepClone();
            req.Remove("wo list");
            RemoveNullSectionsFromWoList(req);
            return root.ToJsonString(CompactJson);
        }

        if (req["woList"] is JsonArray camelWoList)
        {
            req[WoListKey] = camelWoList.DeepClone();
            req.Remove("woList");
            RemoveNullSectionsFromWoList(req);
            return root.ToJsonString(CompactJson);
        }

        // Loose match (ignore spaces/underscores/hyphens etc.)
        foreach (var kv in req.ToList())
        {
            if (JsonLooseKey.NormalizeKeyLoose(kv.Key) == JsonLooseKey.NormalizeKeyLoose(WoListKey) && kv.Value is JsonArray arr)
            {
                req[WoListKey] = arr.DeepClone();
                req.Remove(kv.Key);
                break;
            }
        }

        RemoveNullSectionsFromWoList(req);

        return root.ToJsonString(CompactJson);
    }

    /// <summary>
    /// Detects which journal sections are present in the payload by inspecting non-empty JournalLines arrays.
    /// Returns types in stable order (Item, Expense, Hour).
    /// </summary>
    public static List<Domain.JournalType> DetectJournalTypesPresent(string normalizedWoPayloadJson, string woItemKey, string woExpKey, string woHourKey)
    {
        var set = new HashSet<Domain.JournalType>();

        try
        {
            using var doc = JsonDocument.Parse(normalizedWoPayloadJson);

            if (!doc.RootElement.TryGetProperty(RequestKey, out var reqObj) || reqObj.ValueKind != JsonValueKind.Object)
                return new List<Domain.JournalType>();

            if (!JsonLooseKey.TryGetPropertyLoose(reqObj, WoListKey, out var woListEl) || woListEl.ValueKind != JsonValueKind.Array)
                return new List<Domain.JournalType>();

            foreach (var wo in woListEl.EnumerateArray())
            {
                if (wo.ValueKind != JsonValueKind.Object)
                    continue;

                if (HasNonEmptyJournalLines(wo, woItemKey)) set.Add(Domain.JournalType.Item);
                if (HasNonEmptyJournalLines(wo, woExpKey)) set.Add(Domain.JournalType.Expense);
                if (HasNonEmptyJournalLines(wo, woHourKey)) set.Add(Domain.JournalType.Hour);
            }
        }
        catch
        {
            // tolerant by design
        }

        var ordered = new List<Domain.JournalType>();
        if (set.Contains(Domain.JournalType.Item)) ordered.Add(Domain.JournalType.Item);
        if (set.Contains(Domain.JournalType.Expense)) ordered.Add(Domain.JournalType.Expense);
        if (set.Contains(Domain.JournalType.Hour)) ordered.Add(Domain.JournalType.Hour);
        return ordered;
    }

    private static bool HasNonEmptyJournalLines(JsonElement wo, string sectionKey)
    {
        if (!JsonLooseKey.TryGetPropertyLoose(wo, sectionKey, out var section) || section.ValueKind != JsonValueKind.Object)
            return false;

        if (!JsonLooseKey.TryGetPropertyLoose(section, JournalLinesKey, out var lines) || lines.ValueKind != JsonValueKind.Array)
            return false;

        return lines.GetArrayLength() > 0;
    }
}
