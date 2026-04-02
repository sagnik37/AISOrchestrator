using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services;

public sealed partial class ActivitiesUseCase
{
    internal static PostResult AggregateForEmail(IReadOnlyList<PostResult> postResults)
    {
        if (postResults is null || postResults.Count == 0)
        {
            return new PostResult(
                JournalType: JournalType.Expense,
                IsSuccess: true,
                JournalId: null,
                SuccessMessage: "No posting results were produced.",
                Errors: Array.Empty<PostError>(),
                WorkOrdersBefore: 0,
                WorkOrdersPosted: 0,
                WorkOrdersFiltered: 0,
                ValidationResponseRaw: null);
        }

        var anyFailure = postResults.Any(r => !r.IsSuccess);
        var maxBefore = postResults.Max(r => r.WorkOrdersBefore);
        var maxFiltered = postResults.Max(r => r.WorkOrdersFiltered);
        var conservativePosted = ResolveConservativePostedCount(postResults);
        var allErrors = postResults.SelectMany(r => r.Errors ?? Array.Empty<PostError>()).ToList();
        var rawValidation = postResults.Select(r => r.ValidationResponseRaw).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        var msg = anyFailure
            ? "One or more journal types reported errors during validation/posting."
            : "All journal types completed successfully.";

        return new PostResult(
            JournalType: JournalType.Expense,
            IsSuccess: !anyFailure,
            JournalId: null,
            SuccessMessage: msg,
            Errors: allErrors,
            WorkOrdersBefore: maxBefore,
            WorkOrdersPosted: conservativePosted,
            WorkOrdersFiltered: maxFiltered,
            ValidationResponseRaw: rawValidation);
    }

    internal static int TryGetWorkOrderCount(string woPayloadJson)
    {
        if (string.IsNullOrWhiteSpace(woPayloadJson)) return 0;

        try
        {
            using var doc = JsonDocument.Parse(woPayloadJson);
            return TryReadWorkOrderCount(doc.RootElement);
        }
        catch (Exception ex)
        {
            ThrottledTrace.Debug(
                key: "ActivitiesUseCase.TryGetWorkOrderCount",
                message: "Failed to parse _request.WOList length (best-effort).",
                ex: ex);
            return 0;
        }
    }

    private static int ResolveConservativePostedCount(IReadOnlyList<PostResult> postResults)
    {
        var postedNonZero = postResults.Select(r => r.WorkOrdersPosted).Where(x => x > 0).ToList();
        return postedNonZero.Count > 0 ? postedNonZero.Min() : 0;
    }

    private static int TryReadWorkOrderCount(JsonElement root)
    {
        if (!root.TryGetProperty("_request", out var reqObj) || reqObj.ValueKind != JsonValueKind.Object)
            return 0;

        if (reqObj.TryGetProperty("WOList", out var woList1) && woList1.ValueKind == JsonValueKind.Array)
            return woList1.GetArrayLength();

        if (reqObj.TryGetProperty("wo list", out var woList2) && woList2.ValueKind == JsonValueKind.Array)
            return woList2.GetArrayLength();

        return 0;
    }
}
