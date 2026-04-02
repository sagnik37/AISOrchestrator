using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Resilience;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Utilities;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// FSCM OData client for LegalEntityIntegrationParametersBaseIntParamTables.
/// Used to resolve journal name identifiers for WO integration.
/// </summary>
public sealed class FscmLegalEntityIntegrationParametersHttpClient : IFscmLegalEntityIntegrationParametersClient
{
    private readonly HttpClient _http;
    private readonly FscmOptions _opt;
    private readonly IResilientHttpExecutor _executor;
    private readonly ILogger<FscmLegalEntityIntegrationParametersHttpClient> _logger;

    public FscmLegalEntityIntegrationParametersHttpClient(
        HttpClient http,
        FscmOptions opt,
        IResilientHttpExecutor executor,
        ILogger<FscmLegalEntityIntegrationParametersHttpClient> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _opt = opt ?? throw new ArgumentNullException(nameof(opt));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<LegalEntityJournalNames> GetJournalNamesAsync(RunContext ctx, string dataAreaId, CancellationToken ct)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        if (string.IsNullOrWhiteSpace(dataAreaId)) throw new ArgumentException("dataAreaId is required.", nameof(dataAreaId));

        var company = dataAreaId.Trim();

        var baseUrl = FscmUrlBuilder.ResolveFscmBaseUrl(_opt, null, legacyName: "FscmODataBaseUrl");

        // NOTE:
        // Different environments expose different entity set names and/or column names for the same concept.
        // Entity sets observed:
        // - RPCLegalEntityIntegrationParametersBaseIntParams
        // - LegalEntityIntegrationParametersBaseIntParamTables
        // Column names observed:
        // - RPCItemJournalNameId / RPCHourJournalNameId / RPCExpenseJournalNameId
        // - RPCWOIntInventJournalNameId / RPCWOIntHourJournalNameId / RPCWOIntExpenseJournalNameId
        //
        // We query the preferred entity set first, and fall back to the legacy one if the first returns 0 rows.

        var (names, firstStatus) = await TryFetchAsync(ctx, baseUrl, "RPCLegalEntityIntegrationParametersBaseIntParams", company, ct)
            .ConfigureAwait(false);

        if (names is not null)
            return names;

        // Fallback: legacy entity set name.
        var (fallbackNames, _) = await TryFetchAsync(ctx, baseUrl, "LegalEntityIntegrationParametersBaseIntParamTables", company, ct)
            .ConfigureAwait(false);

        return fallbackNames ?? new LegalEntityJournalNames(null, null, null);
    }

    private async Task<(LegalEntityJournalNames? Names, HttpStatusCode? Status)> TryFetchAsync(
        RunContext ctx,
        string baseUrl,
        string entitySetName,
        string company,
        CancellationToken ct)
    {
        var url = FscmUrlBuilder.BuildUrl(baseUrl, $"/data/{entitySetName}");

        // Select BOTH sets of column names and coalesce during mapping.
        var query = "?" +
                    "$select=dataAreaId," +
                    "ItemJournalNameId,HourJournalNameId,ExpenseJournalNameId&cross-company=true" +

                    $"&$filter=dataAreaId eq '{EscapeODataLiteral(company)}'" +
                    "&$top=1";

        //var query = "?" +
        //            "&cross-company=true" +
        //            $"&$filter=dataAreaId eq '{EscapeODataLiteral(company)}'" +
        //            "&$top=1";

        var full = url + query;

        var sw = Stopwatch.StartNew();
        _logger.LogInformation(
            "FSCM LegalEntityIntegrationParameters fetch START. EntitySet={EntitySet} Url={Url} RunId={RunId} CorrelationId={CorrelationId} Company={Company}",
            entitySetName, full, ctx.RunId, ctx.CorrelationId, company);

        var resp = await _executor.SendAsync(
            _http,
            () => new HttpRequestMessage(HttpMethod.Get, full),
            ctx,
            operationName: "FSCM_LEGAL_ENTITY_INTEGRATION_PARAMS",
            ct).ConfigureAwait(false);

        var body = (await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false)) ?? string.Empty;
        sw.Stop();

        _logger.LogInformation(
            "FSCM LegalEntityIntegrationParameters fetch END. EntitySet={EntitySet} Status={Status} ElapsedMs={ElapsedMs} ResponseBytes={Bytes} RunId={RunId} CorrelationId={CorrelationId}",
            entitySetName, (int)resp.StatusCode, sw.ElapsedMilliseconds, Encoding.UTF8.GetByteCount(body), ctx.RunId, ctx.CorrelationId);

        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new HttpRequestException($"FSCM auth failed ({(int)resp.StatusCode}). Body: {LogText.TrimForLog(body)}", null, resp.StatusCode);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "FSCM LegalEntityIntegrationParameters non-success. EntitySet={EntitySet} Status={Status} Body={Body}",
                entitySetName, (int)resp.StatusCode, LogText.TrimForLog(body));
            return (null, resp.StatusCode);
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
            return (null, resp.StatusCode);

        var row = arr[0];

        static string? TryString(JsonElement obj, string name)
            => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        var expense = TryString(row, "ExpenseJournalNameId") ?? TryString(row, "RPCWOIntExpenseJournalNameId");
        var hour = TryString(row, "HourJournalNameId") ?? TryString(row, "RPCWOIntHourJournalNameId");
        var invent = TryString(row, "ItemJournalNameId") ?? TryString(row, "RPCWOIntInventJournalNameId");

        return (new LegalEntityJournalNames(
            ExpenseJournalNameId: expense,
            HourJournalNameId: hour,
            InventJournalNameId: invent), resp.StatusCode);
    }

    private static string EscapeODataLiteral(string s)
        => (s ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);
}
