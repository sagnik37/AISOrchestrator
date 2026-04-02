// File: .../Core/UseCases/FsaDeltaPayload/*
//
// - Moves delta payload orchestration into Core (UseCase layer) and splits the orchestrator into partials.
// - Functions layer becomes a thin adapter.


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.FsaDeltaPayloadJsonUtil;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload;

internal static class FsaDeltaPayloadJsonValueReaders
{
        private static string? TryJournalDescription(JsonElement row)
        {
            // Prefer explicit journal description field(s) if present.
            if (row.TryGetProperty("msdyn_journaldescription", out var jd) && jd.ValueKind == JsonValueKind.String)
                return jd.GetString();

            // Common fallbacks in orgs:
            if (row.TryGetProperty("msdyn_description", out var d) && d.ValueKind == JsonValueKind.String)
                return d.GetString();

            if (row.TryGetProperty("msdyn_name", out var n) && n.ValueKind == JsonValueKind.String)
                return n.GetString();

            return null;
        }

        private static decimal? TryDecimalAny(JsonElement row, params string[] props)
        {
            foreach (var prop in props)
            {
                var d = TryDecimal(row, prop);
                if (d.HasValue) return d;
            }
            return null;
        }

        internal static bool TryGuid(JsonElement row, string prop, out Guid id)
        {
            id = Guid.Empty;
            if (!row.TryGetProperty(prop, out var p)) return false;
            return p.ValueKind == JsonValueKind.String && Guid.TryParse(p.GetString(), out id);
        }

        private static decimal? TryDecimal(JsonElement row, string prop)
        {
            if (!row.TryGetProperty(prop, out var p)) return null;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var d)) return d;
            return null;
        }

        internal static int? TryInt(JsonElement row, string prop)
        {
            if (!row.TryGetProperty(prop, out var p)) return null;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i)) return i;
            var s = p.ToString();
            return int.TryParse(s, out var j) ? j : null;
        }

        private static string? TryCurrency(JsonElement row)
        {
            if (row.TryGetProperty("isocurrencycode", out var iso) && iso.ValueKind == JsonValueKind.String)
                return iso.GetString();

            if (row.TryGetProperty("transactioncurrencyid", out var cur) &&
                cur.ValueKind == JsonValueKind.Object &&
                cur.TryGetProperty("isocurrencycode", out var code) &&
                code.ValueKind == JsonValueKind.String)
            {
                return code.GetString();
            }

            return null;
        }

        private static string? TryFormattedOrRaw(JsonElement row, string attributeOrLookupLogicalName)
        {
            var formattedKey = attributeOrLookupLogicalName + "@OData.Community.Display.V1.FormattedValue";
            if (row.TryGetProperty(formattedKey, out var f) && f.ValueKind == JsonValueKind.String)
                return f.GetString();

            var lookupFormattedKey = "_" + attributeOrLookupLogicalName + "_value@OData.Community.Display.V1.FormattedValue";
            if (row.TryGetProperty(lookupFormattedKey, out var lf) && lf.ValueKind == JsonValueKind.String)
                return lf.GetString();

            if (row.TryGetProperty(attributeOrLookupLogicalName, out var raw))
                return raw.ValueKind == JsonValueKind.String ? raw.GetString() : raw.ToString();

            var lookupKey = "_" + attributeOrLookupLogicalName + "_value";
            if (row.TryGetProperty(lookupKey, out var lk))
                return lk.ValueKind == JsonValueKind.String ? lk.GetString() : lk.ToString();

            return null;
        }

        private static string? TryFormattedOrGuidAware(JsonElement row, string attributeOrLookupLogicalName)
        {
            // 1) Prefer formatted
            var formattedKey = attributeOrLookupLogicalName + "@OData.Community.Display.V1.FormattedValue";
            if (row.TryGetProperty(formattedKey, out var f) && f.ValueKind == JsonValueKind.String)
                return f.GetString();

            var lookupFormattedKey = "_" + attributeOrLookupLogicalName + "_value@OData.Community.Display.V1.FormattedValue";
            if (row.TryGetProperty(lookupFormattedKey, out var lf) && lf.ValueKind == JsonValueKind.String)
                return lf.GetString();

            // 2) Fallback to raw
            string? raw = null;

            if (row.TryGetProperty(attributeOrLookupLogicalName, out var a))
                raw = a.ValueKind == JsonValueKind.String ? a.GetString() : a.ToString();
            else
            {
                var lookupKey = "_" + attributeOrLookupLogicalName + "_value";
                if (row.TryGetProperty(lookupKey, out var lk))
                    raw = lk.ValueKind == JsonValueKind.String ? lk.GetString() : lk.ToString();
            }

            // 3) If raw looks like GUID, treat as "not a usable label"
            if (LooksLikeGuid(raw))
                return null;

            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }

        private static bool LooksLikeGuid(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();

            // Allow {...}
            if (s.Length >= 2 && s[0] == '{' && s[^1] == '}')
                s = s.Substring(1, s.Length - 2);

            return Guid.TryParse(s, out _);
        }

        private static string? TryWarehouse(JsonElement row)
        {
            // Prefer identifier injected by fetcher (what FSCM expects)
            if (row.TryGetProperty("msdyn_warehouseidentifier", out var wh) && wh.ValueKind == JsonValueKind.String)
            {
                var id = wh.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                    return id;
            }

            // Fallback to formatted lookup label (msdyn_name)
            var formatted = TryFormattedOrRaw(row, "msdyn_warehouse");
            if (!string.IsNullOrWhiteSpace(formatted))
                return formatted;

            // Last fallback: raw GUID
            if (row.TryGetProperty("_rpc_warehouse_value", out var gid) && gid.ValueKind == JsonValueKind.String)
                return gid.GetString();

            return null;
        }

        private static bool? TryStatecodeActive(JsonElement row)
        {
            if (!row.TryGetProperty("statecode", out var sc)) return null;

            if (sc.ValueKind == JsonValueKind.Number && sc.TryGetInt32(out var i))
                return i == 0;

            var s = sc.ToString();
            if (s == "0" || s.Equals("Active", StringComparison.OrdinalIgnoreCase)) return true;
            if (s == "1" || s.Equals("Inactive", StringComparison.OrdinalIgnoreCase)) return false;

            return null;
        }

        private static HashSet<Guid> ExtractWoIdsFromPresence(JsonDocument presenceDoc)
        {
            var set = new HashSet<Guid>();

            if (presenceDoc is null)
                return set;

            if (!presenceDoc.RootElement.TryGetProperty("value", out var value) ||
                value.ValueKind != JsonValueKind.Array)
            {
                return set;
            }

            foreach (var item in value.EnumerateArray())
            {
                if (item.TryGetProperty("_msdyn_workorder_value", out var p) &&
                    p.ValueKind == JsonValueKind.String)
                {
                    var s = p.GetString();
                    if (Guid.TryParse(s, out var g))
                        set.Add(g);
                }
            }

            return set;
        }

        internal static string? TryString(JsonElement row, string prop)
        {
            if (row.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
            return null;
        }
}
