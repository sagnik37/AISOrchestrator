// File: FscmAccountingPeriodResolver.Calendar.cs
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

    private async Task<string> ResolveFiscalCalendarIdAsync(RunContext context, string baseUrl, CancellationToken ct)
    {
        var configured = _opt.FiscalCalendarName?.Trim();
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("FSCM FiscalCalendarName is not configured.");

        if (!string.IsNullOrEmpty(_cachedFiscalCalendarId) &&
            string.Equals(_cachedFiscalCalendarName, configured, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "FSCM Calendar cache HIT. Configured={Configured} Calendar={Calendar} RunId={RunId} CorrelationId={CorrelationId}",
                configured, _cachedFiscalCalendarId, context.RunId, context.CorrelationId);
            return _cachedFiscalCalendarId;
        }

        var entitySet = _opt.FiscalCalendarEntitySet?.Trim();
        if (string.IsNullOrWhiteSpace(entitySet))
            throw new InvalidOperationException("FSCM FiscalCalendarEntitySet is not configured.");

        var calendarIdField = string.IsNullOrWhiteSpace(_opt.FiscalCalendarIdField) ? "CalendarId" : _opt.FiscalCalendarIdField.Trim();

        var nameField = string.IsNullOrWhiteSpace(_opt.FiscalCalendarNameField)
            ? calendarIdField
            : _opt.FiscalCalendarNameField.Trim();

        var calendarId = await TryResolveCalendarIdAsync(context, baseUrl, entitySet, nameField, configured, calendarIdField, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(calendarId) &&
            !string.Equals(nameField, calendarIdField, StringComparison.OrdinalIgnoreCase))
        {
            calendarId = await TryResolveCalendarIdAsync(context, baseUrl, entitySet, calendarIdField, configured, calendarIdField, ct).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(calendarId))
        {
            var candidates = await ListCalendarCandidatesAsync(context, baseUrl, entitySet, calendarIdField, ct).ConfigureAwait(false);
            var hint = candidates.Count == 0 ? "No calendars returned from FSCM." : $"Available CalendarId samples: {string.Join(", ", candidates)}";
            throw new InvalidOperationException($"FSCM fiscal calendar not found. Configured='{configured}'. EntitySet={entitySet}. {hint}");
        }

        _cachedFiscalCalendarId = calendarId;
        _cachedFiscalCalendarName = configured;

        _logger.LogInformation(
            "FSCM Calendar resolved. Configured={Configured} Calendar={Calendar} RunId={RunId} CorrelationId={CorrelationId}",
            configured, calendarId, context.RunId, context.CorrelationId);

        return calendarId;
    }

    private async Task<string?> TryResolveCalendarIdAsync(
        RunContext context,
        string baseUrl,
        string entitySet,
        string filterField,
        string filterValue,
        string selectField,
        CancellationToken ct)
    {
        var filter = $"{filterField} eq '{EscapeODataString(filterValue)}'";

        var url =
            $"{baseUrl.TrimEnd('/')}/data/{entitySet}" +
            $"?$select={Uri.EscapeDataString(selectField)}" +
            $"&$filter={Uri.EscapeDataString(filter)}" +
            $"&$top=1";

        var body = await SendODataAsync(context, "FSCM.Calendar", url, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("value", out var value) &&
            value.ValueKind == JsonValueKind.Array &&
            value.GetArrayLength() > 0)
        {
            var row = value[0];
            if (row.TryGetProperty(selectField, out var ce))
            {
                var id = ce.ValueKind == JsonValueKind.String ? ce.GetString() : ce.ToString();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    _logger.LogInformation(
                        "FSCM Calendar lookup success. FilterField={FilterField} FilterValue={FilterValue} Calendar={Calendar} RunId={RunId} CorrelationId={CorrelationId}",
                        filterField, filterValue, id, context.RunId, context.CorrelationId);
                    return id;
                }
            }
        }

        _logger.LogWarning(
            "FSCM Calendar lookup returned 0 rows. FilterField={FilterField} FilterValue={FilterValue} Select={Select} Url={Url} RunId={RunId} CorrelationId={CorrelationId}",
            filterField, filterValue, selectField, url, context.RunId, context.CorrelationId);

        return null;
    }

    private async Task<List<string>> ListCalendarCandidatesAsync(
        RunContext context,
        string baseUrl,
        string entitySet,
        string calendarIdField,
        CancellationToken ct)
    {
        var url =
            $"{baseUrl.TrimEnd('/')}/data/{entitySet}" +
            $"?$select={Uri.EscapeDataString(calendarIdField)}" +
            $"&$top=10";

        var body = await SendODataAsync(context, "FSCM.CalendarCandidates", url, ct).ConfigureAwait(false);
        var rows = ParseArray(body, "FSCM.CalendarCandidates");

        var list = new List<string>(capacity: Math.Min(rows.Count, 10));
        foreach (var r in rows)
        {
            var s = TryGetString(r, calendarIdField);
            if (!string.IsNullOrWhiteSpace(s))
                list.Add(s);
        }

        return list;
    }
}
