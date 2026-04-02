using System;
using System.Collections.Generic;
using System.Linq;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Processing;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services.Handlers;

public sealed class PostingAuditWriter : IPostingAuditWriter
{
    private readonly IProcessingAuditService _audit;

    public PostingAuditWriter(IProcessingAuditService audit)
    {
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public void WritePostingAudit(
        RunAuditSummary audit,
        IReadOnlyList<PostResult> results,
        IReadOnlyDictionary<JournalType, IReadOnlyList<WoPayloadCandidateWorkOrder>> candidatesByJournal)
    {
        foreach (var result in results)
        {
            var journalType = result.JournalType.ToString();
            var candidates = candidatesByJournal.TryGetValue(result.JournalType, out var list)
                ? list
                : Array.Empty<WoPayloadCandidateWorkOrder>();

            if (result.IsSuccess)
            {
                foreach (var candidate in candidates)
                {
                    _audit.AddSuccess(
                        audit,
                        workOrderNumber: candidate.WorkOrderId,
                        stage: "JournalPosting",
                        workOrderGuid: candidate.WorkOrderGuid,
                        journalType: journalType);
                }

                continue;
            }

            var failureReason = BuildFailureReason(result);
            if (candidates.Count == 0)
            {
                _audit.AddFailure(
                    audit,
                    workOrderNumber: $"(journal:{journalType})",
                    stage: "JournalPosting",
                    reason: failureReason,
                    workOrderGuid: null,
                    journalType: journalType);
            }
            else
            {
                foreach (var candidate in candidates)
                {
                    _audit.AddFailure(
                        audit,
                        workOrderNumber: candidate.WorkOrderId,
                        stage: "JournalPosting",
                        reason: failureReason,
                        workOrderGuid: candidate.WorkOrderGuid,
                        journalType: journalType);
                }
            }
        }
    }

    public void WriteExceptionAudit(RunAuditSummary audit, Exception ex)
    {
        _audit.AddFailure(
            audit,
            workOrderNumber: "(unknown)",
            stage: "PostingException",
            reason: ex.Message,
            workOrderGuid: null,
            journalType: null);
    }

    private static string BuildFailureReason(PostResult result)
    {
        var reasons = new List<string>();

        if (!string.IsNullOrWhiteSpace(result.SuccessMessage))
            reasons.Add(result.SuccessMessage!);

        if (result.Errors is { Count: > 0 })
        {
            reasons.AddRange(result.Errors
                .Select(e => e.Message)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        return reasons.Count == 0 ? "Unknown failure" : string.Join(" | ", reasons);
    }
}
