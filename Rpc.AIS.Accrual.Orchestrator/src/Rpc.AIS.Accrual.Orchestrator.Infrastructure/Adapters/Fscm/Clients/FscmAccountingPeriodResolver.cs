// FscmAccountingPeriodHttpClient.cs 


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

/// <summary>
/// Encapsulates FSCM accounting-period resolution logic (query building, parsing, caching, semantics).
/// Extracted from FscmAccountingPeriodHttpClient to improve SRP. Behavior preserved.
/// </summary>
public sealed partial class FscmAccountingPeriodResolver
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly FscmOptions _endpoints;
    private readonly FscmAccountingPeriodOptions _opt;
    private readonly ILogger<FscmAccountingPeriodResolver> _logger;

    // Cache resolved calendar id/name per process (safe; changes require restart)
    private string? _cachedFiscalCalendarId;
    private string? _cachedFiscalCalendarName;

    // Out-of-window (date -> isClosed) bounded cache
    private readonly object _outOfWindowCacheLock = new();
    private readonly Dictionary<DateOnly, bool> _outOfWindowClosedCache = new();
    private readonly Queue<DateOnly> _outOfWindowCacheOrder = new();

    internal FscmAccountingPeriodResolver(HttpClient http, FscmOptions endpoints, ILogger<FscmAccountingPeriodResolver> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
        _opt = endpoints.Periods ?? throw new ArgumentNullException(nameof(endpoints.Periods));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }


    internal async Task<AccountingPeriodSnapshot> GetSnapshotAsync(RunContext context, CancellationToken ct)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        var baseUrl = ResolveBaseUrlOrThrow();

        // Per new requirement: CalendarId must come from config (e.g., "Fis Cal").
        var fiscalCalendar = _opt.FiscalCalendarIdOverride?.Trim();
        if (string.IsNullOrWhiteSpace(fiscalCalendar))
        {
            throw new InvalidOperationException(
                "Missing configuration: Fscm:Periods:FiscalCalendarIdOverride. " +
                "Per new design, calendar id must be passed from config (example: \"Fis Cal\").");
        }

        // 1) Fetch ALL FiscalPeriods for the calendar (no lookback/lookahead windowing).
        // Implement FetchFiscalPeriodsAllAsync in  ...Periods.cs.
        var periods = await FetchFiscalPeriodsAllAsync(context, baseUrl, fiscalCalendar!, ct).ConfigureAwait(false);
        if (periods.Count == 0)
        {
            throw new InvalidOperationException(
                $"FSCM fiscal period query returned no records. Calendar='{fiscalCalendar}'.");
        }

        // 2) Fetch LedgerFiscalPeriodsV2 statuses for those periods (single in-memory map for the run).
        // Reuse  existing FetchLedgerStatusesAsync - pass "periods" as-is. No per-year windowing changes needed.
        var statusByKey = await FetchLedgerStatusesAsync(
            context,
            baseUrl,
            fiscalCalendar!,
            periods,
            ct).ConfigureAwait(false);

        // 3) Current open period start is kept ONLY for diagnostics/backward compat;
        // logic is NOT anchored to "today" anymore.
        var today = DateTime.UtcNow.Date;
        PeriodRow? currentOpen = null;

        // Prefer: open period containing today; else next open after today; else earliest open.
        for (int i = 0; i < periods.Count; i++)
        {
            var p = periods[i];
            if (today < p.StartDate.Date || today > p.EndDate.Date) continue;

            if (IsOpenStatus(statusByKey, p.Key, context))
            {
                currentOpen = p;
                break;
            }
        }

        if (currentOpen is null)
        {
            // Find next open after today
            for (int i = 0; i < periods.Count; i++)
            {
                var p = periods[i];
                if (p.StartDate.Date <= today) continue;

                if (IsOpenStatus(statusByKey, p.Key, context))
                {
                    currentOpen = p;
                    break;
                }
            }
        }

        if (currentOpen is null)
        {
            // Fallback: earliest open in the list
            for (int i = 0; i < periods.Count; i++)
            {
                var p = periods[i];
                if (IsOpenStatus(statusByKey, p.Key, context))
                {
                    currentOpen = p;
                    break;
                }
            }
        }

        var currentOpenStart = (currentOpen?.StartDate.Date) ?? today;

        // Snapshot bounds are legacy fields — no windowing logic uses them anymore.
        var snapshotMinDate = periods.Min(p => p.StartDate.Date);
        var snapshotMaxDate = periods.Max(p => p.EndDate.Date);

        // 4) Delegate: is date closed?
        ValueTask<bool> IsDateInClosedAsync(DateTime dateUtc, CancellationToken token)
        {
            var d = dateUtc.Date;

            PeriodRow? containing = null;
            for (int i = 0; i < periods.Count; i++)
            {
                var p = periods[i];
                if (d < p.StartDate.Date || d > p.EndDate.Date) continue;
                containing = p;
                break;
            }

            if (containing is null)
                return ValueTask.FromResult(false); // fail-open if not found

            if (!statusByKey.TryGetValue(containing.Key, out var status) || string.IsNullOrWhiteSpace(status))
                return ValueTask.FromResult(false); // fail-open if missing status row

            // CLOSED if not open.
            return ValueTask.FromResult(!IsOpenPeriodStatusValue(status));
        }

        // 5) Delegate: resolve effective transaction date from ops date
        ValueTask<DateTime> ResolveTransactionDateUtcAsync(DateTime opsDateUtc, CancellationToken token)
        {
            var ops = opsDateUtc.Date;

            // Find the period containing ops date
            PeriodRow? containing = null;
            for (int i = 0; i < periods.Count; i++)
            {
                var p = periods[i];
                if (ops < p.StartDate.Date || ops > p.EndDate.Date) continue;
                containing = p;
                break;
            }

            if (containing is null)
                return ValueTask.FromResult(ops); // fail-open

            if (statusByKey.TryGetValue(containing.Key, out var st) && !string.IsNullOrWhiteSpace(st))
            {
                if (IsOpenPeriodStatusValue(st))
                    return ValueTask.FromResult(ops);
            }
            else
            {
                // Missing status row => fail-open
                return ValueTask.FromResult(ops);
            }

            // CLOSED => first date of NEXT OPEN period after this period's end
            var afterEnd = containing.EndDate.Date;

            for (int i = 0; i < periods.Count; i++)
            {
                var p = periods[i];
                if (p.StartDate.Date <= afterEnd) continue;

                if (IsOpenStatus(statusByKey, p.Key, context))
                    return ValueTask.FromResult(p.StartDate.Date);
            }

            // Fallback: earliest open, else currentOpenStart
            for (int i = 0; i < periods.Count; i++)
            {
                var p = periods[i];
                if (IsOpenStatus(statusByKey, p.Key, context))
                    return ValueTask.FromResult(p.StartDate.Date);
            }

            return ValueTask.FromResult(currentOpenStart);
        }

        _logger.LogInformation(
            "FSCM Period snapshot ready (NO WINDOWING). Calendar={Calendar} ConfiguredCalendarName={ConfiguredCalendarName} " +
            "OpenStatusValues=[{OpenValues}] Strategy={ClosedReversalDateStrategy} " +
            "SnapshotMin={MinDate} SnapshotMax={MaxDate} Periods={PeriodCount} StatusRows={StatusCount} " +
            "CurrentOpenStart={CurrentOpenStart} RunId={RunId} CorrelationId={CorrelationId}",
            fiscalCalendar,
            _cachedFiscalCalendarName ?? "",
            string.Join(",", GetOpenStatusValues()),
            "NextOpenPeriodStart",
            snapshotMinDate, snapshotMaxDate,
            periods.Count,
            statusByKey.Count,
            currentOpenStart,
            context.RunId, context.CorrelationId);

        return new AccountingPeriodSnapshot(
            CurrentOpenPeriodStartDate: currentOpenStart,
            ClosedReversalDateStrategy: "NextOpenPeriodStart",
            SnapshotMinDate: snapshotMinDate,
            SnapshotMaxDate: snapshotMaxDate,
            IsDateInClosedPeriodAsync: IsDateInClosedAsync,
            ResolveTransactionDateUtcAsync: ResolveTransactionDateUtcAsync
        );
    }




}

