using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services.Handlers;

public sealed class WoPayloadCandidateExtractor : IWoPayloadCandidateExtractor
{
    public IReadOnlyDictionary<JournalType, IReadOnlyList<WoPayloadCandidateWorkOrder>> ExtractByJournal(string woPayloadJson)
    {
        var result = new Dictionary<JournalType, IReadOnlyList<WoPayloadCandidateWorkOrder>>
        {
            [JournalType.Item] = new List<WoPayloadCandidateWorkOrder>(),
            [JournalType.Expense] = new List<WoPayloadCandidateWorkOrder>(),
            [JournalType.Hour] = new List<WoPayloadCandidateWorkOrder>()
        };

        if (string.IsNullOrWhiteSpace(woPayloadJson))
            return result;

        try
        {
            using var doc = JsonDocument.Parse(woPayloadJson);
            if (!doc.RootElement.TryGetProperty("_request", out var request) || request.ValueKind != JsonValueKind.Object ||
                !request.TryGetProperty("WOList", out var woList) || woList.ValueKind != JsonValueKind.Array)
                return result;

            var items = (List<WoPayloadCandidateWorkOrder>)result[JournalType.Item];
            var expenses = (List<WoPayloadCandidateWorkOrder>)result[JournalType.Expense];
            var hours = (List<WoPayloadCandidateWorkOrder>)result[JournalType.Hour];

            foreach (var wo in woList.EnumerateArray())
            {
                if (wo.ValueKind != JsonValueKind.Object)
                    continue;

                var workOrderId = TryGetString(wo, "WorkOrderID") ?? TryGetString(wo, "WorkorderID") ?? "(unknown)";
                var workOrderGuid = TryGetString(wo, "WorkOrderGUID") ?? TryGetString(wo, "WorkorderGUID");
                var candidate = new WoPayloadCandidateWorkOrder(workOrderId, workOrderGuid);

                AddIfPresent(items, wo, "WOItemLines", candidate);
                AddIfPresent(expenses, wo, "WOExpLines", candidate);
                AddIfPresent(hours, wo, "WOHourLines", candidate);
            }
        }
        catch
        {
            // Best effort only. Audit extraction must never break posting.
        }

        return result;
    }

    private static void AddIfPresent(List<WoPayloadCandidateWorkOrder> list, JsonElement wo, string sectionName, WoPayloadCandidateWorkOrder candidate)
    {
        if (!HasJournalLines(wo, sectionName))
            return;

        if (!list.Any(x => string.Equals(x.WorkOrderId, candidate.WorkOrderId, StringComparison.OrdinalIgnoreCase)))
            list.Add(candidate);
    }

    private static bool HasJournalLines(JsonElement wo, string sectionName)
    {
        if (!wo.TryGetProperty(sectionName, out var section) || section.ValueKind != JsonValueKind.Object)
            return false;

        if (!section.TryGetProperty("JournalLines", out var lines) || lines.ValueKind != JsonValueKind.Array)
            return false;

        return lines.GetArrayLength() > 0;
    }

    private static string? TryGetString(JsonElement obj, string propertyName)
    {
        if (obj.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            var value = prop.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }
}
