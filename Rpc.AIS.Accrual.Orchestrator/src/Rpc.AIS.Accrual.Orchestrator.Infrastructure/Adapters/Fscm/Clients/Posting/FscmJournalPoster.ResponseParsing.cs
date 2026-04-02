using System;
using System.Collections.Generic;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;

public sealed partial class FscmJournalPoster
{
    private static bool TryExtractJournalIdFromResponse(string responseBody, out string journalId, ILogger logger, RunContext ctx)
    {
        journalId = string.Empty;

        if (string.IsNullOrWhiteSpace(responseBody))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (!root.TryGetProperty("WOList", out var woList) || woList.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var wo in woList.EnumerateArray())
            {
                if (TryExtractFromSection(wo, "WOItemLines", out journalId)) return true;
                if (TryExtractFromSection(wo, "WOExpLines", out journalId)) return true;
                if (TryExtractFromSection(wo, "WOHourLines", out journalId)) return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "FSCM journal validation/create response did not contain a parsable JournalId. RunId={RunId} CorrelationId={CorrelationId}.",
                ctx.RunId,
                ctx.CorrelationId);
            return false;
        }
    }

    private static bool TryExtractFromSection(JsonElement wo, string sectionName, out string id)
    {
        id = string.Empty;

        if (!wo.TryGetProperty(sectionName, out var section) || section.ValueKind != JsonValueKind.Object)
            return false;

        if (!section.TryGetProperty("JournalId", out var jProp))
            return false;

        id = jProp.ValueKind switch
        {
            JsonValueKind.String => jProp.GetString() ?? string.Empty,
            JsonValueKind.Number => jProp.GetRawText(),
            _ => string.Empty
        };

        id = id.Trim();
        return id.Length > 0;
    }

    private static bool TryExtractJournalPostsFromCreateResponse(
        string responseBody,
        string company,
        out List<JournalPostRow> journalPosts,
        ILogger logger,
        RunContext ctx)
    {
        journalPosts = new List<JournalPostRow>();

        if (string.IsNullOrWhiteSpace(responseBody))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (!root.TryGetProperty("WOList", out var woList) || woList.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var wo in woList.EnumerateArray())
            {
                TryAppendJournalPostRow(wo, "WOItemLines", company, JournalType.Item, journalPosts);
                TryAppendJournalPostRow(wo, "WOExpLines", company, JournalType.Expense, journalPosts);
                TryAppendJournalPostRow(wo, "WOHourLines", company, JournalType.Hour, journalPosts);
            }

            return journalPosts.Count > 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "FSCM create response did not contain parsable JournalIds. RunId={RunId} CorrelationId={CorrelationId}.",
                ctx.RunId,
                ctx.CorrelationId);
            return false;
        }
    }

    private static void TryAppendJournalPostRow(
        JsonElement woEl,
        string sectionName,
        string company,
        JournalType journalType,
        ICollection<JournalPostRow> target)
    {
        if (!woEl.TryGetProperty(sectionName, out var secEl))
            return;

        if (secEl.ValueKind == JsonValueKind.Null || secEl.ValueKind == JsonValueKind.Undefined)
            return;

        if (secEl.ValueKind != JsonValueKind.Object)
            return;

        var journalId = secEl.TryGetProperty("JournalId", out var idEl) && idEl.ValueKind == JsonValueKind.String
            ? idEl.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(journalId))
            return;

        target.Add(new JournalPostRow(company, journalId!.Trim(), journalType.ToString()));
    }
}
