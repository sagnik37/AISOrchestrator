using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;

/// <summary>
/// Applies the fiscal period rule:
/// - RPCWorkingDate (Work/Ops date from FS) remains the original operations date.
/// - TransactionDate is resolved by period status:
///     OPEN  => TransactionDate = RPCWorkingDate
///     CLOSED/ON-HOLD => TransactionDate = StartDate of the NEXT OPEN period (based on ops date’s period)
///
/// Uses the AccountingPeriodSnapshot resolver (single source of truth).
/// </summary>
public sealed class PayloadPostingDateAdjuster
{
    private readonly IFscmAccountingPeriodClient _periodClient;
    private readonly ILogger<PayloadPostingDateAdjuster> _logger;

    public PayloadPostingDateAdjuster(
        IFscmAccountingPeriodClient periodClient,
        ILogger<PayloadPostingDateAdjuster> logger)
    {
        _periodClient = periodClient ?? throw new ArgumentNullException(nameof(periodClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> AdjustAsync(RunContext ctx, string payloadJson, CancellationToken ct)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        if (string.IsNullOrWhiteSpace(payloadJson)) return payloadJson;

        // Parse early so we can map Company -> RunContext.DataAreaId for period status lookups.
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(payloadJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Payload JSON parse failed. Proceeding without TransactionDate adjustment. RunId={RunId} CorrelationId={CorrelationId}",
                ctx.RunId, ctx.CorrelationId);
            return payloadJson;
        }

        var woList = root?["_request"]?["WOList"] as JsonArray;

        // Try to infer legal entity / company from the first WO row (FieldService payloads carry Company).
        var company = woList?
            .OfType<JsonObject>()
            .Select(wo => JsonLooseKey.TryGetStringLoose(wo, "Company", out var c) ? c : null)
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

        var effectiveCtx = ctx;
        if (!string.IsNullOrWhiteSpace(company) && string.IsNullOrWhiteSpace(ctx.DataAreaId))
        {
            effectiveCtx = ctx with { DataAreaId = company!.Trim() };
        }

        AccountingPeriodSnapshot snapshot;
        try
        {
            snapshot = await _periodClient.GetSnapshotAsync(effectiveCtx, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Fail-open: do not block posting if period lookup fails.
            _logger.LogWarning(ex,
                "FSCM accounting period snapshot failed. Proceeding without TransactionDate adjustment. RunId={RunId} CorrelationId={CorrelationId}",
                effectiveCtx.RunId, effectiveCtx.CorrelationId);
            return payloadJson;
        }

        if (woList is null || woList.Count == 0)
            return payloadJson;

        var uniqueWorkingDates = new HashSet<DateOnly>();
        var touchedLines = 0;
        var adjustedLines = 0;

        foreach (var woNode in woList.OfType<JsonObject>())
        {
            var (t1, a1) = await AdjustJournalLinesArrayAsync(woNode, "WOItemLines", snapshot, ctx, uniqueWorkingDates, ct).ConfigureAwait(false);
            touchedLines += t1; adjustedLines += a1;

            var (t2, a2) = await AdjustJournalLinesArrayAsync(woNode, "WOExpLines", snapshot, ctx, uniqueWorkingDates, ct).ConfigureAwait(false);
            touchedLines += t2; adjustedLines += a2;

            var (t3, a3) = await AdjustJournalLinesArrayAsync(woNode, "WOHourLines", snapshot, ctx, uniqueWorkingDates, ct).ConfigureAwait(false);
            touchedLines += t3; adjustedLines += a3;
        }

        _logger.LogInformation(
            "TransactionDate adjustment complete. LinesTouched={Touched} LinesAdjusted={Adjusted} UniqueWorkingDates={Dates} CurrentOpenPeriodStart={OpenStart} RunId={RunId} CorrelationId={CorrelationId}",
            touchedLines,
            adjustedLines,
            uniqueWorkingDates.Count,
            snapshot.CurrentOpenPeriodStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ctx.RunId,
            ctx.CorrelationId);

        return root!.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private async Task<(int touched, int adjusted)> AdjustJournalLinesArrayAsync(
    JsonObject woNode,
    string journalKey,
    AccountingPeriodSnapshot snapshot,
    RunContext ctx,
    HashSet<DateOnly> uniqueWorkingDates,
    CancellationToken ct)
    {
        var journalObj = woNode[journalKey] as JsonObject;
        if (journalObj is null) return (0, 0);

        var lines = journalObj["JournalLines"] as JsonArray;
        if (lines is null || lines.Count == 0) return (0, 0);

        var touched = 0;
        var adjusted = 0;

        foreach (var lineObj in lines.OfType<JsonObject>())
        {
            // FS sends ops date; preserve it as OperationDate.
            // Prefer OperationDate, fallback to RPCWorkingDate, then TransactionDate.
            var workingLiteral =
                (string?)lineObj["OperationDate"]
                ?? (string?)lineObj["RPCWorkingDate"]
                ?? (string?)lineObj["TransactionDate"];

            if (string.IsNullOrWhiteSpace(workingLiteral))
                continue;

            if (!TryParseFscmDateLiteral(workingLiteral!, out var workingUtc))
                continue;

            var workingDate = DateOnly.FromDateTime(workingUtc);
            uniqueWorkingDates.Add(workingDate);
            touched++;

            DateTime resolvedTxUtc;
            try
            {
                // Snapshot decides: if workingUtc is in closed period => returns next open period start (UTC midnight).
                // If open => returns workingUtc (or same day).
                resolvedTxUtc = await snapshot.ResolveTransactionDateUtcAsync(workingUtc, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "TransactionDate resolve failed. Treating as OPEN (TransactionDate=WorkingDate). WorkingDate={WorkingDate} RunId={RunId} CorrelationId={CorrelationId}",
                    workingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ctx.RunId,
                    ctx.CorrelationId);

                resolvedTxUtc = workingUtc.Date;
            }

            var resolvedTxDate = DateOnly.FromDateTime(resolvedTxUtc.Date);

            // - OperationDate stays as FS ops date (workingDate)
            // - TransactionDate becomes next-open-start when closed, else equals workingDate
            lineObj["OperationDate"] = ToFscmDateLiteral(workingDate);
            lineObj["TransactionDate"] = ToFscmDateLiteral(resolvedTxDate);

            if (resolvedTxDate != workingDate)
                adjusted++;
        }

        return (touched, adjusted);
    }

    private static bool TryParseFscmDateLiteral(string literal, out DateTime utc)
    {
        // Expected: /Date(1700000000000)/
        utc = default;
        if (string.IsNullOrWhiteSpace(literal)) return false;

        var s = literal.Trim();
        if (!s.StartsWith("/Date(", StringComparison.OrdinalIgnoreCase)) return false;
        var end = s.IndexOf(")/", StringComparison.OrdinalIgnoreCase);
        if (end < 0) return false;

        var numPart = s.Substring(6, end - 6);
        if (!long.TryParse(numPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
            return false;

        utc = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        return true;
    }

    private static string ToFscmDateLiteral(DateOnly d)
    {
        var dt = new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
        var ms = new DateTimeOffset(dt).ToUnixTimeMilliseconds();
        return $"/Date({ms})/";
    }
}
