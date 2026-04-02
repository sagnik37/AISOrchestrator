// File: FscmAccountingPeriodResolver.Http.cs
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



    private string ResolveBaseUrlOrThrow()
    {
        var baseUrl = _endpoints.ResolveBaseUrl(_endpoints.BaseUrl);
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("FSCM base URL missing. Configure 'Endpoints:BaseUrl'.");
        return baseUrl;
    }

    /// <summary>
    /// Executes resolve fiscal calendar id async.
    /// </summary>

    private async Task<string> SendODataAsync(RunContext context, string step, string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(context.RunId))
            req.Headers.TryAddWithoutValidation("x-run-id", context.RunId);
        if (!string.IsNullOrWhiteSpace(context.CorrelationId))
            req.Headers.TryAddWithoutValidation("x-correlation-id", context.CorrelationId);

        _logger.LogInformation(
            "{Step} START. Url={Url} RunId={RunId} CorrelationId={CorrelationId}",
            step, url, context.RunId, context.CorrelationId);

        var sw = Stopwatch.StartNew();
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        var body = (await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false)) ?? string.Empty;
        sw.Stop();

        _logger.LogInformation(
            "{Step} END. Status={Status} ElapsedMs={ElapsedMs} Bytes={Bytes} RunId={RunId} CorrelationId={CorrelationId}",
            step, (int)resp.StatusCode, sw.ElapsedMilliseconds, body.Length, context.RunId, context.CorrelationId);

        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException($"{step} unauthorized/forbidden. HTTP {(int)resp.StatusCode}. Body: {Trim(body)}");

        if ((int)resp.StatusCode == 429 || (int)resp.StatusCode >= 500)
            throw new HttpRequestException($"{step} transient failure {(int)resp.StatusCode}. Body: {Trim(body)}", null, resp.StatusCode);

        if ((int)resp.StatusCode >= 400)
            throw new InvalidOperationException($"{step} failed {(int)resp.StatusCode}. Body: {Trim(body)}");

        return body;
    }

    // ----------------------- Parsing helpers -----------------------

    // <summary>
    // Executes parse array.
    // </summary>
}
