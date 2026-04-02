using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.FscmJournalPolicies;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

public sealed partial class FscmJournalFetchHttpClient
{
    private List<FscmJournalLine> ParseODataValueArrayToJournalLines(
        string json,
        IFscmJournalFetchPolicy policy,
        string workOrderLineIdField)
        => new List<FscmJournalLine>(_rowMapper.MapMany(json, policy, workOrderLineIdField));

    private static IEnumerable<List<Guid>> Chunk(List<Guid> ids, int chunkSize)
    {
        if (chunkSize <= 0) chunkSize = 25;
        for (var i = 0; i < ids.Count; i += chunkSize)
            yield return ids.GetRange(i, Math.Min(chunkSize, ids.Count - i));
    }

    private static Guid? TryGetGuidLoose(JsonElement obj, string propName)
    {
        if (!obj.TryGetProperty(propName, out var p))
            return null;

        if (p.ValueKind == JsonValueKind.String)
        {
            var s = p.GetString();
            if (Guid.TryParse(s, out var g)) return g;
        }

        // Some FSCM OData responses serialize GUID fields as plain text but not always as JSON strings;
        // ToString() is safe for all kinds.
        var t = p.ToString();
        return Guid.TryParse(t, out var g2) ? g2 : null;
    }

    /// <summary>
    /// Executes try get guid.
    /// </summary>
    private static Guid? TryGetGuid(JsonElement obj, string propName)
    {
        if (!obj.TryGetProperty(propName, out var p))
            return null;

        if (p.ValueKind == JsonValueKind.String)
        {
            var s = p.GetString();
            if (Guid.TryParse(s, out var g)) return g;
        }
        return null;
    }

    /// <summary>
    /// Executes try get decimal.
    /// </summary>
    private static decimal? TryGetDecimal(JsonElement obj, string propName)
    {
        if (!obj.TryGetProperty(propName, out var p))
            return null;

        return p.ValueKind switch
        {
            JsonValueKind.Number when p.TryGetDecimal(out var d) => d,
            JsonValueKind.String => TryParseDecimal(p.GetString()),
            _ => null
        };

        static decimal? TryParseDecimal(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
        }
    }

    /// <summary>
    /// Executes try get date.
    /// </summary>
    private static DateTime? TryGetDate(JsonElement obj, string propName)
    {
        if (!obj.TryGetProperty(propName, out var p))
            return null;

        if (p.ValueKind == JsonValueKind.String)
        {
            var s = p.GetString();
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                return dt;
        }

        return null;
    }

    /// <summary>
    /// Executes try get string.
    /// </summary>
    private static string? TryGetString(JsonElement obj, string propName)
    {
        if (!obj.TryGetProperty(propName, out var p))
            return null;

        return p.ValueKind switch
        {
            JsonValueKind.String => p.GetString(),
            JsonValueKind.Number => p.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static bool? TryGetBool(JsonElement obj, string propName)
    {
        if (!obj.TryGetProperty(propName, out var p))
            return null;

        return p.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => ParseBoolLoose(p.GetString()),
            JsonValueKind.Number => p.TryGetInt32(out var i) ? i != 0 : (bool?)null,
            _ => null
        };

        static bool? ParseBoolLoose(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (bool.TryParse(s, out var b)) return b;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i != 0;
            return null;
        }
    }

    /// <summary>
    /// Executes trim.
    /// </summary>
    private static string Trim(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        const int max = 4000;
        return s.Length <= max ? s : string.Concat(s.AsSpan(0, max), " ...");
    }
}
