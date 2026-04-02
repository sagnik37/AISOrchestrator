using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.FscmJournalPolicies;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

public sealed partial class FscmJournalFetchHttpClient
{
    private async Task<IReadOnlyList<FscmJournalLine>> FetchSingleUrlAsync(
        RunContext context,
        JournalType journalType,
        string entitySet,
        string url,
        IFscmJournalFetchPolicy policy,
        string workOrderLineIdField,
        CancellationToken ct,
        bool allowSelectFallback = true)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Propagate correlation (best effort)
        if (!string.IsNullOrWhiteSpace(context.RunId))
            req.Headers.TryAddWithoutValidation("x-run-id", context.RunId);
        if (!string.IsNullOrWhiteSpace(context.CorrelationId))
            req.Headers.TryAddWithoutValidation("x-correlation-id", context.CorrelationId);

        _logger.LogInformation(
            "FSCM OData fetch START. JournalType={JournalType} EntitySet={EntitySet} Url={Url} RunId={RunId} CorrelationId={CorrelationId}",
            journalType, entitySet, url, context.RunId, context.CorrelationId);

        var sw = Stopwatch.StartNew();
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        var body = (await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false)) ?? string.Empty;
        sw.Stop();

        _logger.LogInformation(
            "FSCM OData fetch END. Status={Status} JournalType={JournalType} EntitySet={EntitySet} ElapsedMs={ElapsedMs} Bytes={Bytes} RunId={RunId} CorrelationId={CorrelationId}",
            (int)resp.StatusCode, journalType, entitySet, sw.ElapsedMilliseconds, body.Length, context.RunId, context.CorrelationId);

        // Auth failures => fail-fast
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogError(
                "FSCM OData fetch AUTH failure. Status={Status} JournalType={JournalType} EntitySet={EntitySet} Body={Body} RunId={RunId} CorrelationId={CorrelationId}",
                (int)resp.StatusCode, journalType, entitySet, Trim(body), context.RunId, context.CorrelationId);

            throw new UnauthorizedAccessException(
                $"FSCM OData unauthorized/forbidden for {entitySet}. HTTP {(int)resp.StatusCode}. Body: {Trim(body)}");
        }

        // Transient => durable retry
        if ((int)resp.StatusCode == 429 || (int)resp.StatusCode >= 500)
        {
            _logger.LogWarning(
                "FSCM OData fetch transient failure. Status={Status} JournalType={JournalType} EntitySet={EntitySet} Body={Body} RunId={RunId} CorrelationId={CorrelationId}",
                (int)resp.StatusCode, journalType, entitySet, Trim(body), context.RunId, context.CorrelationId);

            throw new HttpRequestException(
                $"Transient FSCM OData fetch failure {(int)resp.StatusCode} {resp.ReasonPhrase}. EntitySet={entitySet}. Body: {Trim(body)}",
                null,
                resp.StatusCode);
        }

        // Other 4xx => non-transient => treat as "no data" but log
        if ((int)resp.StatusCode >= 400 && (int)resp.StatusCode <= 499)
        {
            _logger.LogWarning(
                "FSCM OData fetch non-transient failure. Status={Status} JournalType={JournalType} EntitySet={EntitySet} Body={Body} RunId={RunId} CorrelationId={CorrelationId}",
                (int)resp.StatusCode, journalType, entitySet, Trim(body), context.RunId, context.CorrelationId);

            // Some FSCM environments don't expose all fields used in the policy's $select (e.g., optional amounts for price normalization).
            // If we detect a missing-field OData error, retry once with the policy's fallback select.
            if (allowSelectFallback && policy is FscmJournalFetchPolicyBase basePolicy)
            {
                var fallbackSelect = basePolicy.SelectFallback;
                if (!string.IsNullOrWhiteSpace(fallbackSelect)
                    && !string.Equals(fallbackSelect, policy.Select, StringComparison.Ordinal)
                    && LooksLikeMissingSelectFieldError(body))
                {
                    var fallbackUrl = ReplaceSelect(url, fallbackSelect);
                    _logger.LogWarning(
                        "Retrying FSCM OData fetch with fallback $select due to missing-field error. JournalType={JournalType} EntitySet={EntitySet} RunId={RunId} CorrelationId={CorrelationId}",
                        journalType, entitySet, context.RunId, context.CorrelationId);

                    return await FetchSingleUrlAsync(context, journalType, entitySet, fallbackUrl, policy, workOrderLineIdField, ct, allowSelectFallback: false)
                        .ConfigureAwait(false);
                }
            }

            return Array.Empty<FscmJournalLine>();
        }

        return ParseODataValueArrayToJournalLines(body, policy, workOrderLineIdField);
    }

    /// <summary>
    /// Executes resolve base url or throw.
    /// </summary>
    private string ResolveBaseUrlOrThrow()
    {
        var baseUrl = _endpoints.ResolveBaseUrl(_endpoints.BaseUrl);
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("FSCM base URL missing. Configure 'Endpoints:BaseUrl'.");
        return baseUrl;
    }

    private static bool LooksLikeMissingSelectFieldError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;

        // Common OData messages for missing properties/fields across FO versions
        return body.Contains("Cannot find property", StringComparison.OrdinalIgnoreCase)
            || body.Contains("Could not find a property named", StringComparison.OrdinalIgnoreCase)   // 
            || body.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
            || body.Contains("is not declared", StringComparison.OrdinalIgnoreCase)
            || (body.Contains("$select", StringComparison.OrdinalIgnoreCase) && body.Contains("property", StringComparison.OrdinalIgnoreCase));
    }

    private static string ReplaceSelect(string url, string newSelect)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(newSelect)) return url;

        // Replace $select=... up to next '&' (or end)
        return System.Text.RegularExpressions.Regex.Replace(
            url,
            @"(\$select=)([^&]+)", //  FIX (CS1009): verbatim string avoids invalid escape sequence
            m => m.Groups[1].Value + newSelect,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
