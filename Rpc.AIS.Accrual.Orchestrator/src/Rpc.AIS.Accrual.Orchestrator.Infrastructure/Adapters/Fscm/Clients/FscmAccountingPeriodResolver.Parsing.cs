// File: FscmAccountingPeriodResolver.Parsing.cs
// split helper responsibilities into partial files (behavior preserved).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Utils;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

public sealed partial class FscmAccountingPeriodResolver
{

    private List<JsonElement> ParseArray(string json, string stepForLogs)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            _logger.LogWarning("{Step}: Empty response body.", stepForLogs);
            return new List<JsonElement>(0);
        }

        json = json.TrimStart('\uFEFF', '\u200B', '\u0000').Trim();

        try
        {
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                _logger.LogWarning("{Step}: Root is not an object. RootKind={Kind}. BodyPreview={Preview}",
                    stepForLogs, doc.RootElement.ValueKind, json.Length <= 400 ? json : json.Substring(0, 400));
                return new List<JsonElement>(0);
            }

            if (!doc.RootElement.TryGetProperty("value", out var value))
            {
                if (doc.RootElement.TryGetProperty("error", out var err))
                {
                    _logger.LogWarning("{Step}: Response contains 'error' and no 'value'. Error={Error}. BodyPreview={Preview}",
                        stepForLogs, err.ToString(), json.Length <= 400 ? json : json.Substring(0, 400));
                }
                else
                {
                    _logger.LogWarning("{Step}: No 'value' property in response. BodyPreview={Preview}",
                        stepForLogs, json.Length <= 400 ? json : json.Substring(0, 400));
                }
                return new List<JsonElement>(0);
            }

            if (value.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("{Step}: 'value' is not an array. Kind={Kind}. BodyPreview={Preview}",
                    stepForLogs, value.ValueKind, json.Length <= 400 ? json : json.Substring(0, 400));
                return new List<JsonElement>(0);
            }

            var list = new List<JsonElement>(value.GetArrayLength());
            foreach (var row in value.EnumerateArray())
                list.Add(row.Clone()); // critical

            _logger.LogInformation("{Step}: Parsed value array. Count={Count}", stepForLogs, list.Count);
            return list;
        }
        catch (JsonException jex)
        {
            _logger.LogError(jex, "{Step}: JSON parse failed. BodyPreview={Preview}",
                stepForLogs, json.Length <= 400 ? json : json.Substring(0, 400));
            return new List<JsonElement>(0);
        }
    }

    /// <summary>
    /// Executes try get date time utc.
    /// </summary>
    private static DateTime? TryGetDateTimeUtc(JsonElement obj, string propName)
    {
        if (string.IsNullOrWhiteSpace(propName)) return null;
        if (!obj.TryGetProperty(propName, out var p)) return null;

        if (p.ValueKind == JsonValueKind.String)
        {
            var s = p.GetString();
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
                return dto.UtcDateTime;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }

        return null;
    }

    /// <summary>
    /// Executes try get string.
    /// </summary>
    private static string? TryGetString(JsonElement obj, string propName)
    {
        if (string.IsNullOrWhiteSpace(propName)) return null;
        if (!obj.TryGetProperty(propName, out var p)) return null;

        return p.ValueKind switch
        {
            JsonValueKind.String => p.GetString(),
            JsonValueKind.Number => p.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    /// <summary>
    /// Executes count missing status.
    /// </summary>
    private static int CountMissingStatus(List<PeriodRow> periods, IReadOnlyDictionary<string, string?> statuses)
    {
        int missing = 0;
        for (int i = 0; i < periods.Count; i++)
        {
            if (!statuses.ContainsKey(periods[i].Key))
                missing++;
        }
        return missing;
    }

    /// <summary>
    /// Executes to o data date time offset literal unquoted.
    /// </summary>
    private static string ToODataDateTimeOffsetLiteralUnquoted(DateTime utcDateTime)
    {
        var utc = utcDateTime.Kind == DateTimeKind.Utc
            ? utcDateTime
            : DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);

        return $"{utc:yyyy-MM-ddTHH:mm:ssZ}";
    }

    private static string EscapeODataString(string s) => s.Replace("'", "''");

    /// <summary>
    /// Executes trim.
    /// </summary>
    private static string Trim(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        const int max = 4000;
        return s.Length <= max ? s : string.Concat(s.AsSpan(0, max), " ...");

    }

    /// <summary>
    /// Executes period key.
    /// </summary>
    private static string PeriodKey(string calendar, string fiscalYear, string periodName)
        => $"{calendar}||{fiscalYear}||{periodName}";

    /// <summary>
    /// Carries period row data.
    /// </summary>
    private sealed record PeriodRow(string Calendar, string FiscalYear, string PeriodName, DateTime StartDate, DateTime EndDate)
    {
        public string Key => PeriodKey(Calendar, FiscalYear, PeriodName);
    }
}
