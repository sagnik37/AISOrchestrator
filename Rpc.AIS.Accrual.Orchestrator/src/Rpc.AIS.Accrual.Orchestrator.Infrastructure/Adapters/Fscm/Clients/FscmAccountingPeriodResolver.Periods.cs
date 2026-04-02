// File: FscmAccountingPeriodResolver.Periods.cs
// split helper responsibilities into partial files (behavior preserved).

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

public sealed partial class FscmAccountingPeriodResolver
{


    private async Task<List<PeriodRow>> FetchFiscalPeriodsAllAsync(
      RunContext context,
      string baseUrl,
      string fiscalCalendar,
      CancellationToken ct)
    {
        var entitySet = _opt.FiscalPeriodEntitySet?.Trim();
        if (string.IsNullOrWhiteSpace(entitySet))
            throw new InvalidOperationException("FSCM FiscalPeriodEntitySet is not configured.");

        var calendarField = string.IsNullOrWhiteSpace(_opt.FiscalPeriodCalendarField) ? "Calendar" : _opt.FiscalPeriodCalendarField.Trim();
        var yearField = string.IsNullOrWhiteSpace(_opt.FiscalPeriodYearField) ? "FiscalYear" : _opt.FiscalPeriodYearField.Trim();
        var nameField = string.IsNullOrWhiteSpace(_opt.FiscalPeriodNameField) ? "PeriodName" : _opt.FiscalPeriodNameField.Trim();
        var startField = string.IsNullOrWhiteSpace(_opt.FiscalPeriodStartDateField) ? "StartDate" : _opt.FiscalPeriodStartDateField.Trim();
        var endField = string.IsNullOrWhiteSpace(_opt.FiscalPeriodEndDateField) ? "EndDate" : _opt.FiscalPeriodEndDateField.Trim();

        var select = string.Join(",", calendarField, yearField, nameField, startField, endField);

        // Calendar is STRING (e.g. "Fis Cal") -> quote it
        var cal = EscapeODataString(fiscalCalendar);

        var filter = $"{calendarField} eq '{cal}'";

        var url =
            $"{baseUrl.TrimEnd('/')}/data/{entitySet}" +
            $"?$select={Uri.EscapeDataString(select)}" +
            $"&$filter={Uri.EscapeDataString(filter)}";

        var body = await SendODataAsync(context, "FSCM.Periods", url, ct).ConfigureAwait(false);
        var rows = ParseArray(body, "FSCM.Periods");

        var list = new List<PeriodRow>(rows.Count);
        foreach (var r in rows)
        {
            var periodName = TryGetString(r, nameField);
            var fiscalYear = TryGetString(r, yearField);
            var calendar = TryGetString(r, calendarField);

            var start = TryGetDateTimeUtc(r, startField);
            var end = TryGetDateTimeUtc(r, endField);

            if (string.IsNullOrWhiteSpace(periodName) ||
                string.IsNullOrWhiteSpace(fiscalYear) ||
                string.IsNullOrWhiteSpace(calendar) ||
                !start.HasValue ||
                !end.HasValue)
            {
                continue;
            }

            list.Add(new PeriodRow(
                Calendar: calendar.Trim(),
                FiscalYear: fiscalYear.Trim(),
                PeriodName: periodName.Trim(),
                StartDate: start.Value.Date,
                EndDate: end.Value.Date));
        }

        list.Sort(static (a, b) =>
        {
            var c = a.StartDate.CompareTo(b.StartDate);
            if (c != 0) return c;
            c = string.Compare(a.FiscalYear, b.FiscalYear, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            return string.Compare(a.PeriodName, b.PeriodName, StringComparison.OrdinalIgnoreCase);
        });

        // Use actual bounds for logging only (not used for logic)
        var minDate = list.Count > 0 ? list[0].StartDate : DateTime.MinValue.Date;
        var maxDate = list.Count > 0 ? list[^1].EndDate : DateTime.MinValue.Date;

        _logger.LogInformation(
            "FSCM Periods resolved (ALL). Calendar={Calendar} Periods={Count} Range={MinDate}->{MaxDate} RunId={RunId} CorrelationId={CorrelationId}",
            fiscalCalendar, list.Count, minDate, maxDate, context.RunId, context.CorrelationId);

        return list;
    }


    // : This is the method  GetSnapshotAsync expects (6 args).
    // It returns a dictionary keyed by PeriodKey (Calendar||FiscalYear||PeriodName) => PeriodStatus string.
    private async Task<Dictionary<string, string?>> FetchLedgerStatusesAsync(
        RunContext context,
        string baseUrl,
        string fiscalCalendar,
        List<PeriodRow> periods,
        CancellationToken ct)
    {
        var entitySet = _opt.LedgerFiscalPeriodEntitySet?.Trim();
        if (string.IsNullOrWhiteSpace(entitySet))
            throw new InvalidOperationException("FSCM LedgerFiscalPeriodEntitySet is not configured.");

        var yearField = string.IsNullOrWhiteSpace(_opt.LedgerFiscalPeriodYearField) ? "YearName" : _opt.LedgerFiscalPeriodYearField.Trim();
        var periodField = string.IsNullOrWhiteSpace(_opt.LedgerFiscalPeriodPeriodField) ? "PeriodName" : _opt.LedgerFiscalPeriodPeriodField.Trim();
        var statusField = string.IsNullOrWhiteSpace(_opt.LedgerFiscalPeriodStatusField) ? "PeriodStatus" : _opt.LedgerFiscalPeriodStatusField.Trim();
        var calendarField = string.IsNullOrWhiteSpace(_opt.LedgerFiscalPeriodCalendarField) ? "Calendar" : _opt.LedgerFiscalPeriodCalendarField.Trim();

        var cal = (fiscalCalendar ?? "").Trim();

        // Key by PeriodKey so other parts of this file stay simple
        var map = new Dictionary<string, string?>(capacity: periods.Count);

        // Query only needed years (FiscalYear from FiscalPeriods maps to YearName in LedgerFiscalPeriodsV2)
        var years = periods
            .Select(p => p.FiscalYear)
            .Where(y => !string.IsNullOrWhiteSpace(y))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

                var select = string.Join(",", calendarField, yearField, periodField, statusField);

        foreach (var year in years)
        {
            var filterSb = new StringBuilder();
           filterSb.Append(calendarField).Append(" eq '").Append(EscapeODataString(cal)).Append("' and ")
        .Append(yearField).Append(" eq '").Append(EscapeODataString(year)).Append("'");

            // IMPORTANT: constrain by company (dataAreaId) to match FSCM validation/posting context.
            if (!string.IsNullOrWhiteSpace(context.DataAreaId))
            {
                filterSb.Append(" and LegalEntityId eq '")
                        .Append(EscapeODataString(context.DataAreaId.Trim()))
                        .Append("'");
            }

            var url =
                $"{baseUrl.TrimEnd('/')}/data/{entitySet}" +
                $"?$select={Uri.EscapeDataString(select)}" +
                $"&$filter={Uri.EscapeDataString(filterSb.ToString())}";

            string body;
            try
            {
                body = await SendODataAsync(context, "FSCM.LedgerStatus", url, ct).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("failed 404", StringComparison.OrdinalIgnoreCase) ||
                                          ex.Message.Contains("404", StringComparison.OrdinalIgnoreCase))
            {
                // Fail-closed: without ledger status rows we cannot safely determine open/closed.
                throw new InvalidOperationException(
                    $"FSCM.LedgerStatus returned 404 for EntitySet='{entitySet}', Calendar='{cal}', Year='{year}'. " +
                    "Cannot safely determine posting periods; aborting to prevent invalid posting dates.",
                    ex);
            }
            var rows = ParseArray(body, "FSCM.LedgerStatus");
            foreach (var r in rows)
            {
                var yr = TryGetString(r, yearField);
                var pn = TryGetString(r, periodField);
                var st = TryGetString(r, statusField);

                if (string.IsNullOrWhiteSpace(yr) || string.IsNullOrWhiteSpace(pn))
                    continue;

                // Build PeriodKey compatible with PeriodRow.Key
                var key = PeriodKey(cal, yr.Trim(), pn.Trim());

                if (!map.ContainsKey(key))
                    map[key] = st?.Trim();
            }

            _logger.LogInformation(
                "FSCM Ledger statuses fetched (year batch). EntitySet={EntitySet} Calendar={Calendar} Year={Year} Rows={Rows} MapCount={MapCount} RunId={RunId} CorrelationId={CorrelationId}",
                entitySet, cal, year, rows.Count, map.Count, context.RunId, context.CorrelationId);
        }

        return map;
    }

    private bool IsOpenStatus(
     IReadOnlyDictionary<string, string?> statusByPeriodKey,
     string periodKey,
     RunContext context)
    {
        if (!statusByPeriodKey.TryGetValue(periodKey, out var status) || string.IsNullOrWhiteSpace(status))
        {
            // Fail-closed: if we cannot prove "open", treat as NOT open so posting date is shifted safely.
            _logger.LogWarning(
                "FSCM PeriodStatus treated as CLOSED because status row missing. PeriodKey={PeriodKey} RunId={RunId} CorrelationId={CorrelationId}",
                periodKey, context.RunId, context.CorrelationId);

            return false;
        }

        var isOpen = IsOpenPeriodStatusValue(status);

        _logger.LogInformation(
            "FSCM PeriodStatus evaluated. PeriodKey={PeriodKey} PeriodStatus={PeriodStatus} IsOpen={IsOpen} OpenStatusValues=[{OpenValues}] RunId={RunId} CorrelationId={CorrelationId}",
            periodKey, status, isOpen, string.Join(",", GetOpenStatusValues()), context.RunId, context.CorrelationId);

        return isOpen;
    }

    /// <summary>
    /// Executes is open period status value.
    /// </summary>
    private bool IsOpenPeriodStatusValue(string status)
    {
        var openValues = GetOpenStatusValues();
        var s = status?.Trim() ?? string.Empty;

        for (int i = 0; i < openValues.Count; i++)
        {
            if (string.Equals(s, openValues[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Executes get open status values.
    /// </summary>
    private List<string> GetOpenStatusValues()
    {
        // Prefer list if provided; else single value; else default "Open".
        if (_opt.OpenPeriodStatusValues is { Count: > 0 })
        {
            return _opt.OpenPeriodStatusValues
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(_opt.OpenPeriodStatusValue))
            return new List<string> { _opt.OpenPeriodStatusValue.Trim() };

        return new List<string> { "Open" };
    }

    // <summary>
    // Executes send o data async.
    // </summary>
}
