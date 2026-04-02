using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services.Summaries;

public sealed partial class WorkOrderProcessingSummaryBuilder : IWorkOrderProcessingSummaryBuilder
{
    public WorkOrderProcessingSummary Build(string woPayloadJson, IReadOnlyList<PostResult> postResults, IReadOnlyList<string>? generalErrors)
    {
        var workOrders = ParseWorkOrders(woPayloadJson);
        var dataIssues = BuildDataIssues(workOrders);
        var errorDetails = BuildErrorDetails(postResults, generalErrors, dataIssues);

        var openWorkOrders = workOrders.Count;
        var createdInFscmSuccessfully = ResolveConservativeCreatedCount(postResults);
        var validatedWorkOrders = createdInFscmSuccessfully;
        var notValidatedWorkOrders = Math.Max(0, openWorkOrders - validatedWorkOrders);

        return new WorkOrderProcessingSummary(
            OpenWorkOrders: openWorkOrders,
            DataIssueWorkOrders: dataIssues.Count,
            ValidatedWorkOrders: validatedWorkOrders,
            NotValidatedWorkOrders: notValidatedWorkOrders,
            CreatedInFscmSuccessfully: createdInFscmSuccessfully,
            ErrorWorkOrders: errorDetails.Count,
            ErrorDetails: errorDetails,
            DataIssues: dataIssues);
    }

    private static List<PayloadWorkOrder> ParseWorkOrders(string woPayloadJson)
    {
        var result = new List<PayloadWorkOrder>();
        if (string.IsNullOrWhiteSpace(woPayloadJson))
            return result;

        try
        {
            using var doc = JsonDocument.Parse(woPayloadJson);
            if (!doc.RootElement.TryGetProperty("_request", out var request) || request.ValueKind != JsonValueKind.Object)
                return result;

            if (!request.TryGetProperty("WOList", out var woList) || woList.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var wo in woList.EnumerateArray())
            {
                if (wo.ValueKind != JsonValueKind.Object)
                    continue;

                var workOrderName = FirstNonBlankString(wo, "WorkOrderID", "WorkorderID", "WorkOrderNumber") ?? "(unknown)";
                var workOrderGuid = FirstNonBlankString(wo, "WorkOrderGUID", "WorkorderGUID");
                var subProjectId = FirstNonBlankString(wo, "SubProjectId", "SubProjectID", "rpc_subprojectid", "SubProject");

                result.Add(new PayloadWorkOrder(workOrderName, workOrderGuid, subProjectId));
            }
        }
        catch
        {
            // Best effort only. Summary generation must never break orchestration completion.
        }

        return result;
    }

    private static List<WorkOrderDataIssueDetail> BuildDataIssues(IEnumerable<PayloadWorkOrder> workOrders)
    {
        return workOrders
            .Where(wo => string.IsNullOrWhiteSpace(wo.SubProjectId))
            .Select(wo => new WorkOrderDataIssueDetail(
                WorkOrderName: wo.WorkOrderName,
                WorkOrderGuid: wo.WorkOrderGuid,
                IssueReason: "Missing subproject",
                IssueDetails: "SubProjectId is missing or blank in the outbound AIS work order payload."))
            .ToList();
    }

    private static List<WorkOrderErrorDetail> BuildErrorDetails(
        IReadOnlyList<PostResult> postResults,
        IReadOnlyList<string>? generalErrors,
        IReadOnlyList<WorkOrderDataIssueDetail> dataIssues)
    {
        var results = new Dictionary<string, WorkOrderErrorDetail>(StringComparer.OrdinalIgnoreCase);

        foreach (var issue in dataIssues)
        {
            results[BuildKey(issue.WorkOrderName, issue.WorkOrderGuid)] = new WorkOrderErrorDetail(
                WorkOrderName: issue.WorkOrderName,
                WorkOrderGuid: issue.WorkOrderGuid,
                ErrorReason: issue.IssueReason,
                ErrorDetails: issue.IssueDetails);
        }

        foreach (var postResult in postResults ?? Array.Empty<PostResult>())
        {
            foreach (var error in postResult.Errors ?? Array.Empty<PostError>())
            {
                if (TryParseValidationError(error, out var parsed))
                {
                    var detail = new WorkOrderErrorDetail(
                        WorkOrderName: parsed.WorkOrderName,
                        WorkOrderGuid: parsed.WorkOrderGuid,
                        ErrorReason: parsed.InfoMessage,
                        ErrorDetails: string.IsNullOrWhiteSpace(parsed.Errors)
                            ? error.Message ?? string.Empty
                            : parsed.Errors);

                    results[BuildKey(detail.WorkOrderName, detail.WorkOrderGuid)] = detail;
                    continue;
                }

                var fallbackKey = BuildFallbackBatchKey(postResult.JournalType, error.Code);
                if (!results.ContainsKey(fallbackKey))
                {
                    results[fallbackKey] = new WorkOrderErrorDetail(
                        WorkOrderName: $"(batch:{postResult.JournalType})",
                        WorkOrderGuid: null,
                        ErrorReason: string.IsNullOrWhiteSpace(error.Code) ? "Posting error" : error.Code,
                        ErrorDetails: error.Message ?? string.Empty);
                }
            }
        }

        if (generalErrors is not null)
        {
            var index = 0;
            foreach (var generalError in generalErrors.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                var key = $"general-{index++}";
                results[key] = new WorkOrderErrorDetail(
                    WorkOrderName: "(run)",
                    WorkOrderGuid: null,
                    ErrorReason: "General orchestration error",
                    ErrorDetails: generalError.Trim());
            }
        }

        return results.Values
            .OrderBy(x => x.WorkOrderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.WorkOrderGuid, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int ResolveConservativeCreatedCount(IReadOnlyList<PostResult> postResults)
    {
        if (postResults is null || postResults.Count == 0)
            return 0;

        var postedCounts = postResults.Select(r => r.WorkOrdersPosted).Where(x => x > 0).ToList();
        return postedCounts.Count > 0 ? postedCounts.Min() : 0;
    }

    private static string? FirstNonBlankString(JsonElement obj, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!obj.TryGetProperty(propertyName, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.String)
            {
                var s = value.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    return s.Trim();
            }
        }

        return null;
    }

    private static string BuildKey(string workOrderName, string? workOrderGuid)
        => $"{workOrderName.Trim()}::{(workOrderGuid ?? string.Empty).Trim()}";

    private static string BuildFallbackBatchKey(JournalType journalType, string code)
        => $"batch::{journalType}::{code}";

    private static bool TryParseValidationError(PostError error, out ParsedValidationError parsed)
    {
        parsed = default;

        if (error is null || !string.Equals(error.Code, "FSCM_VALIDATION_FAILED_WO", StringComparison.OrdinalIgnoreCase))
            return false;

        var message = (error.Message ?? string.Empty).Trim();
        if (message.Length == 0)
            return false;

        var match = ValidationFailureRegex().Match(message);
        if (!match.Success)
            return false;

        parsed = new ParsedValidationError(
            WorkOrderName: match.Groups["wo"].Value.Trim(),
            WorkOrderGuid: match.Groups["guid"].Value.Trim(),
            InfoMessage: match.Groups["info"].Value.Trim(),
            Errors: match.Groups["errs"].Success ? match.Groups["errs"].Value.Trim() : string.Empty);

        return true;
    }

    [GeneratedRegex(
        @"WO validation failed\.\s*WorkOrderGUID=(?<guid>[^,]+),\s*WO Number=(?<wo>[^.]+)\.\s*Info=(?<info>.*?)(?:\s+Errors:\s*(?<errs>.*))?$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex ValidationFailureRegex();

    private sealed record PayloadWorkOrder(string WorkOrderName, string? WorkOrderGuid, string? SubProjectId);
    private sealed record ParsedValidationError(string WorkOrderName, string WorkOrderGuid, string InfoMessage, string Errors);
}
