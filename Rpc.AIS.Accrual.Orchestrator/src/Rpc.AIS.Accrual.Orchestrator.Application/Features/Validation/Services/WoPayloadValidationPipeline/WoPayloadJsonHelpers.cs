using System;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.WoPayloadValidationPipeline;

/// <summary>
/// Small JSON helpers for WO payload validation.
/// Kept separate so mappers/validators don't re-implement parsing logic.
/// </summary>
internal static class WoPayloadJsonHelpers
{
    private static readonly Regex FscmDateRegex = new(@"\/Date\((\-?\d+)\)\/", RegexOptions.Compiled);

    internal static string? TryGetString(JsonElement obj, string key)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        if (!obj.TryGetProperty(key, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Null) return null;
        var s = p.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    internal static Guid? TryGetGuid(JsonElement obj, string key)
    {
        var s = TryGetString(obj, key);
        if (string.IsNullOrWhiteSpace(s)) return null;

        s = s.Trim();
        if (s.Length >= 2 && s[0] == '{' && s[^1] == '}')
            s = s.Substring(1, s.Length - 2);

        return Guid.TryParse(s, out var g) ? g : null;
    }

    internal static bool TryGetNumber(JsonElement obj, string key, out decimal value)
    {
        value = 0m;
        if (obj.ValueKind != JsonValueKind.Object) return false;
        if (!obj.TryGetProperty(key, out var p)) return false;
        if (p.ValueKind == JsonValueKind.Number)
            return p.TryGetDecimal(out value);

        if (p.ValueKind == JsonValueKind.String)
        {
            var s = p.GetString();
            return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value)
                   || decimal.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out value);
        }

        return false;
    }
    internal static bool TryGetNonEmptyString(JsonElement obj, string name, out string value)
    {
        value = string.Empty;

        if (!obj.TryGetProperty(name, out var p))
            return false;

        if (p.ValueKind != JsonValueKind.String)
            return false;

        value = p.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
    public static bool TryGetDecimal(JsonElement obj, string name, out decimal value)
    {
        value = 0m;

        if (!obj.TryGetProperty(name, out var p))
            return false;

        if (p.ValueKind == JsonValueKind.Number)
            return p.TryGetDecimal(out value);

        if (p.ValueKind == JsonValueKind.String)
            return decimal.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);

        return false;
    }

    /// <summary>
    /// Parses TransactionDate / rpc_OperationsDate that may be either:
    /// - FSCM style: /Date(1767052800000)/
    /// - ISO-8601: 2026-01-31 or 2026-01-31T00:00:00Z
    /// </summary>
    public static bool TryParseFscmOrIsoDate(string raw, out DateTime utc)
    {
        utc = default;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var m = FscmDateRegex.Match(raw);
        if (m.Success && long.TryParse(m.Groups[1].Value, out var ms))
        {
            utc = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
            return true;
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
        {
            utc = dto.UtcDateTime;
            return true;
        }

        return false;
    }
}
