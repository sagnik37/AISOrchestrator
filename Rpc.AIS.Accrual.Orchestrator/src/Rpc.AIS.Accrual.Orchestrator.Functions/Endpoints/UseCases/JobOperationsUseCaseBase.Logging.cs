using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

public abstract partial class JobOperationsUseCaseBase
{
    protected sealed record WoPayloadTelemetrySummary(
        int Bytes,
        int WorkOrders,
        string WorkOrderIdsCsv,
        string CompaniesCsv,
        string SubProjectIdsCsv);

    protected sealed record PostResultsTelemetrySummary(
        int ResultGroups,
        int SuccessGroups,
        int FailureGroups,
        int WorkOrdersBefore,
        int WorkOrdersPosted,
        int WorkOrdersFiltered,
        int RetryableWorkOrders,
        int RetryableLines,
        int ErrorCount,
        string JournalTypesCsv,
        string FailedJournalTypesCsv);

    protected static WoPayloadTelemetrySummary SummarizeWoPayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return new WoPayloadTelemetrySummary(0, 0, string.Empty, string.Empty, string.Empty);

        var workOrderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var companies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var subProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (!doc.RootElement.TryGetProperty("_request", out var req) || req.ValueKind != JsonValueKind.Object)
                return new WoPayloadTelemetrySummary(payloadJson.Length, 0, string.Empty, string.Empty, string.Empty);

            if (!req.TryGetProperty("WOList", out var woList) || woList.ValueKind != JsonValueKind.Array)
                return new WoPayloadTelemetrySummary(payloadJson.Length, 0, string.Empty, string.Empty, string.Empty);

            foreach (var wo in woList.EnumerateArray())
            {
                if (wo.ValueKind != JsonValueKind.Object) continue;

                if (TryGetStringProperty(wo, "WorkOrderID", out var workOrderId) && !string.IsNullOrWhiteSpace(workOrderId))
                    workOrderIds.Add(workOrderId!);
                else if (TryGetStringProperty(wo, "WorkOrderGuid", out var workOrderGuid) && !string.IsNullOrWhiteSpace(workOrderGuid))
                    workOrderIds.Add(workOrderGuid!);
                else if (TryGetStringProperty(wo, "WorkOrderGUID", out var workOrderGuid2) && !string.IsNullOrWhiteSpace(workOrderGuid2))
                    workOrderIds.Add(workOrderGuid2!);

                if (TryGetStringProperty(wo, "Company", out var company) && !string.IsNullOrWhiteSpace(company))
                    companies.Add(company!);

                if (TryGetStringProperty(wo, "SubProjectId", out var subProjectId) && !string.IsNullOrWhiteSpace(subProjectId))
                    subProjects.Add(subProjectId!);
            }

            return new WoPayloadTelemetrySummary(
                payloadJson.Length,
                workOrderIds.Count > 0 ? workOrderIds.Count : woList.GetArrayLength(),
                string.Join(',', workOrderIds.Take(20)),
                string.Join(',', companies.Take(10)),
                string.Join(',', subProjects.Take(20)));
        }
        catch
        {
            return new WoPayloadTelemetrySummary(payloadJson.Length, 0, string.Empty, string.Empty, string.Empty);
        }
    }

    protected static PostResultsTelemetrySummary SummarizePostResults(IReadOnlyList<PostResult>? postResults)
    {
        if (postResults is null || postResults.Count == 0)
        {
            return new PostResultsTelemetrySummary(0, 0, 0, 0, 0, 0, 0, 0, 0, string.Empty, string.Empty);
        }

        var failed = postResults.Where(x => !x.IsSuccess).Select(x => x.JournalType.ToString()).Distinct(StringComparer.OrdinalIgnoreCase);

        return new PostResultsTelemetrySummary(
            ResultGroups: postResults.Count,
            SuccessGroups: postResults.Count(x => x.IsSuccess),
            FailureGroups: postResults.Count(x => !x.IsSuccess),
            WorkOrdersBefore: postResults.Sum(x => x.WorkOrdersBefore),
            WorkOrdersPosted: postResults.Sum(x => x.WorkOrdersPosted),
            WorkOrdersFiltered: postResults.Sum(x => x.WorkOrdersFiltered),
            RetryableWorkOrders: postResults.Sum(x => x.RetryableWorkOrders),
            RetryableLines: postResults.Sum(x => x.RetryableLines),
            ErrorCount: postResults.Sum(x => x.Errors?.Count ?? 0),
            JournalTypesCsv: string.Join(',', postResults.Select(x => x.JournalType.ToString()).Distinct(StringComparer.OrdinalIgnoreCase)),
            FailedJournalTypesCsv: string.Join(',', failed));
    }

    private static bool TryGetStringProperty(JsonElement obj, string name, out string? value)
    {
        value = null;

        foreach (var prop in obj.EnumerateObject())
        {
            if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;

            value = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
            return true;
        }

        return false;
    }
}
