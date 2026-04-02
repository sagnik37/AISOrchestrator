using System;
using System.Collections.Generic;
using System.Linq;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;

/// <summary>
/// Defines i post error aggregator behavior.
/// </summary>
public interface IPostErrorAggregator
{
    List<PostError> Build(IReadOnlyList<PostError> validationErrors, int removedDueToNoSection, JournalType journalType);
    List<PostError> AddHttpError(List<PostError> errors, HttpPostOutcome outcome);
    List<PostError> AddParseErrors(List<PostError> errors, IReadOnlyList<PostError> parseErrors);
}

/// <summary>
/// Provides post error aggregator behavior.
/// </summary>
public sealed class PostErrorAggregator : IPostErrorAggregator
{
    /// <summary>
    /// Executes build.
    /// </summary>
    public List<PostError> Build(IReadOnlyList<PostError> validationErrors, int removedDueToNoSection, JournalType journalType)
    {
        var list = (validationErrors ?? Array.Empty<PostError>()).ToList();
        if (removedDueToNoSection > 0)
        {
            list.Add(new PostError(
                Code: "WO_SECTION_PRUNED",
                Message: $"Projected payload for {journalType} pruned {removedDueToNoSection} work orders with missing/empty journal section.",
                StagingId: null,
                JournalId: null,
                JournalDeleted: false,
                DeleteMessage: null));
        }
        return list;
    }

    /// <summary>
    /// Executes add http error.
    /// </summary>
    public List<PostError> AddHttpError(List<PostError> errors, HttpPostOutcome outcome)
    {
        errors ??= new List<PostError>();
        if (outcome.StatusCode is >= System.Net.HttpStatusCode.OK and <= (System.Net.HttpStatusCode)299)
            return errors;

        errors.Add(new PostError(
            Code: "FSCM_POST_HTTP_ERROR",
            Message: $"FSCM posting returned HTTP {(int)outcome.StatusCode} ({outcome.StatusCode}). Url={outcome.Url}",
            StagingId: null,
            JournalId: null,
            JournalDeleted: false,
            DeleteMessage: outcome.Body));

        return errors;
    }

    /// <summary>
    /// Executes add parse errors.
    /// </summary>
    public List<PostError> AddParseErrors(List<PostError> errors, IReadOnlyList<PostError> parseErrors)
    {
        errors ??= new List<PostError>();
        if (parseErrors is { Count: > 0 }) errors.AddRange(parseErrors);
        return errors;
    }
}
