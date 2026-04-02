using System;
using System.Globalization;
using System.Text.Json;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.Mappers;

internal static class FsaDeltaPayloadJsonHelpers
{
    internal static bool TryGuid(JsonElement row, string prop, out Guid value)
    {
        value = default;
        if (!TryGetPropertyLoose(row, prop, out var el) || el.ValueKind != JsonValueKind.String) return false;
        var s = el.GetString();
        return Guid.TryParse(s, out value);
    }

    internal static decimal? TryDecimal(JsonElement row, string prop)
    {
        if (!TryGetPropertyLoose(row, prop, out var el)) return null;

        if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d)) return d;

        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d2)) return d2;
        }

        return null;
    }

    internal static decimal? TryDecimalAny(JsonElement row, params string[] props)
    {
        foreach (var p in props)
        {
            var d = TryDecimal(row, p);
            if (d.HasValue) return d;
        }
        return null;
    }
    internal static string? TryLookupFormattedPreferred(JsonElement row, string lookupField)
    {
        var formatted = lookupField + "@OData.Community.Display.V1.FormattedValue";
        if (TryGetPropertyLoose(row, formatted, out var f) && f.ValueKind == JsonValueKind.String)
            return f.GetString();

        if (TryGetPropertyLoose(row, lookupField, out var raw) && raw.ValueKind == JsonValueKind.String)
            return raw.GetString();

        return null;
    }

    internal static string? TryCurrency(JsonElement row)
    {
        // direct field
        if (TryGetPropertyLoose(row, "isocurrencycode", out var c) && c.ValueKind == JsonValueKind.String)
            return c.GetString();

        // expanded nav: transactioncurrencyid($select=isocurrencycode)
        if (TryGetPropertyLoose(row, "transactioncurrencyid", out var nav) && nav.ValueKind == JsonValueKind.Object)
        {
            if (nav.TryGetProperty("isocurrencycode", out var code) && code.ValueKind == JsonValueKind.String)
                return code.GetString();
        }

        // formatted fallback (sometimes exists on lookup field)
        var formatted = "transactioncurrencyid@OData.Community.Display.V1.FormattedValue";
        if (TryGetPropertyLoose(row, formatted, out var f) && f.ValueKind == JsonValueKind.String)
            return f.GetString();

        return null;
    }


    internal static string? TryFormattedOrRaw(JsonElement row, string prop)
    {
        if (TryGetPropertyLoose(row, prop, out var el))
        {
            if (el.ValueKind == JsonValueKind.String) return el.GetString();
            if (el.ValueKind == JsonValueKind.Number) return el.GetRawText();
        }

        // formatted value (Dataverse)
        var formattedProp = prop + "@OData.Community.Display.V1.FormattedValue";
        if (TryGetPropertyLoose(row, formattedProp, out var f) && f.ValueKind == JsonValueKind.String)
            return f.GetString();

        return null;
    }

    internal static string? GetFormattedOnly(JsonElement row, string lookupField)
    {
        // Expect caller to pass the lookup raw property name (e.g. "_rpc_departments_value")
        var formatted = lookupField + "@OData.Community.Display.V1.FormattedValue";
        if (TryGetPropertyLoose(row, formatted, out var f) && f.ValueKind == JsonValueKind.String)
            return f.GetString();
        return null;
    }

    internal static string? TryWarehouse(JsonElement row)
    {
        // Prefer the warehouse identifier injected by the FSA fetch/enrichment pipeline.
        if (TryGetPropertyLoose(row, "msdyn_warehouseidentifier", out var identifier) &&
            identifier.ValueKind == JsonValueKind.String)
        {
            var value = identifier.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        // Next prefer the Dataverse formatted lookup label instead of the raw GUID.
        var formatted = "_msdyn_warehouse_value@OData.Community.Display.V1.FormattedValue";
        if (TryGetPropertyLoose(row, formatted, out var formattedEl) &&
            formattedEl.ValueKind == JsonValueKind.String)
        {
            var value = formattedEl.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        // Fall back to the raw value only when nothing better is available.
        if (TryGetPropertyLoose(row, "_msdyn_warehouse_value", out var raw) &&
            raw.ValueKind == JsonValueKind.String)
        {
            var value = raw.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    internal static string? TrySite(JsonElement row)
        => TryFormattedOrRaw(row, "msdyn_site");

    internal static bool TryStatecodeActive(JsonElement row)
    {
        // statecode == 0 is Active in Dataverse by default
        if (!TryGetPropertyLoose(row, "statecode", out var el)) return true;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i)) return i == 0;
        return true;
    }

    internal static string? TryJournalDescription(JsonElement row)
    {
        // Only respect actual FS description fields.
        // Do NOT fallback to msdyn_name or other values.

        if (TryGetPropertyLoose(row, "msdyn_journaldescription", out var jd) &&
            jd.ValueKind == JsonValueKind.String)
        {
            return jd.GetString();
        }

        if (TryGetPropertyLoose(row, "msdyn_description", out var d) &&
            d.ValueKind == JsonValueKind.String)
        {
            return d.GetString();
        }

        return null;
    }
    internal static DateTime? TryDateTimeUtc(JsonElement row, string propertyName)
    {
        if (!TryGetPropertyLoose(row, propertyName, out var prop))
            return null;

        if (prop.ValueKind == JsonValueKind.Null)
            return null;

        if (prop.ValueKind == JsonValueKind.String)
        {
            var s = prop.GetString();
            if (string.IsNullOrWhiteSpace(s))
                return null;

            if (DateTime.TryParse(
                    s,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out var dt))
            {
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }
        }

        return null;
    }
    internal static bool? TryBool(JsonElement row, string prop)
    {
        if (!TryGetPropertyLoose(row, prop, out var el)) return null;

        if (el.ValueKind == JsonValueKind.True) return true;
        if (el.ValueKind == JsonValueKind.False) return false;

        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i))
            return i != 0;

        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (bool.TryParse(s, out var b)) return b;
            if (int.TryParse(s, out var j)) return j != 0;
        }

        return null;
    }

    private static bool TryGetPropertyLoose(JsonElement row, string prop, out JsonElement value)
    {
        if (row.ValueKind == JsonValueKind.Object)
        {
            if (row.TryGetProperty(prop, out value))
                return true;

            foreach (var p in row.EnumerateObject())
            {
                if (string.Equals(p.Name, prop, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
