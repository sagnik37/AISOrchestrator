using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;

public sealed partial class WoPostingPreparationPipeline
{
    private async Task<string> InjectJournalNamesIfMissingAsync(
        RunContext ctx,
        JournalType journalType,
        string payloadJson,
        CancellationToken ct)
    {
        var mutableDoc = JsonNode.Parse(payloadJson);
        if (mutableDoc is null)
            return payloadJson;

        var woArray = mutableDoc["_request"]?["WOList"]?.AsArray();
        if (woArray is null || woArray.Count == 0)
            return payloadJson;

        var journalMap = await LoadJournalNameMapAsync(ctx, woArray, ct).ConfigureAwait(false);
        if (journalMap.Count == 0)
            return payloadJson;

        foreach (var woNode in woArray.OfType<JsonObject>())
        {
            ApplyJournalNameIfMissing(woNode, journalType, journalMap);
        }

        return mutableDoc.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private async Task<Dictionary<string, object>> LoadJournalNameMapAsync(
        RunContext ctx,
        JsonArray woArray,
        CancellationToken ct)
    {
        var companies = woArray
            .Select(wo => wo?["Company"]?.GetValue<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var journalMap = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var company in companies)
        {
            var result = await _leParams.GetJournalNamesAsync(ctx, company!, ct).ConfigureAwait(false);
            if (result is not null)
                journalMap[company!] = result;
        }

        return journalMap;
    }

    private static void ApplyJournalNameIfMissing(
        JsonObject woNode,
        JournalType journalType,
        IReadOnlyDictionary<string, object> journalMap)
    {
        var company = woNode["Company"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(company))
            return;

        if (!journalMap.TryGetValue(company!, out var resultObj))
            return;

        dynamic result = resultObj;

        switch (journalType)
        {
            case JournalType.Item:
                if (HasLines(woNode, "WOItemLines") && IsBlank(woNode, "WOItemLines"))
                    woNode["WOItemLines"]!["JournalName"] = (string?)result.InventJournalNameId ?? string.Empty;
                break;

            case JournalType.Expense:
                if (HasLines(woNode, "WOExpLines") && IsBlank(woNode, "WOExpLines"))
                    woNode["WOExpLines"]!["JournalName"] = (string?)result.ExpenseJournalNameId ?? string.Empty;
                break;

            case JournalType.Hour:
                if (HasLines(woNode, "WOHourLines") && IsBlank(woNode, "WOHourLines"))
                    woNode["WOHourLines"]!["JournalName"] = (string?)result.HourJournalNameId ?? string.Empty;
                break;
        }
    }

    private static bool HasLines(JsonObject wo, string sectionKey)
    {
        var section = wo[sectionKey] as JsonObject;
        var lines = section?["JournalLines"] as JsonArray;
        return lines is not null && lines.Count > 0;
    }

    private static bool IsBlank(JsonObject wo, string sectionKey)
    {
        var section = wo[sectionKey] as JsonObject;
        var val = section?["JournalName"]?.GetValue<string>();
        return string.IsNullOrWhiteSpace(val);
    }
}
