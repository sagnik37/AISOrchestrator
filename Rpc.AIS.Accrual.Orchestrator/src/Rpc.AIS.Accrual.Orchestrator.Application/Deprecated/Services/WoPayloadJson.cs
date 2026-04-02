namespace Rpc.AIS.Accrual.Orchestrator.Core.Services;

using System.Text.Json;

/// <summary>
/// Shared JsonElement helpers for WO payload navigation.
/// Kept centralized so journal policies and validation engine can reuse without duplicating parsing logic.
/// </summary>
internal static class WoPayloadJson
{
    internal static string? TryGetString(JsonElement obj, string key)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        if (!obj.TryGetProperty(key, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Null) return null;
        var s = p.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    internal static bool TryGetNumber(JsonElement obj, string key, out decimal value)
    {
        value = 0m;
        if (obj.ValueKind != JsonValueKind.Object) return false;
        if (!obj.TryGetProperty(key, out var p)) return false;
        if (p.ValueKind == JsonValueKind.Number)
            return p.TryGetDecimal(out value);

        if (p.ValueKind == JsonValueKind.String)
            return decimal.TryParse(p.GetString(), out value);

        return false;
    }
}
