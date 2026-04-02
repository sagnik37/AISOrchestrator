using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Provides fscm single work order http client behavior.
/// </summary>
public sealed class FscmSingleWorkOrderHttpClient : ISingleWorkOrderPostingClient
{
    private readonly HttpClient _http;
    private readonly FscmOptions _opt;
    private readonly ILogger<FscmSingleWorkOrderHttpClient> _logger;

    public FscmSingleWorkOrderHttpClient(
        HttpClient http,
        FscmOptions opt,
        ILogger<FscmSingleWorkOrderHttpClient> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _opt = opt ?? throw new ArgumentNullException(nameof(opt));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // Back-compat (no RunContext)
    /// <summary>
    /// Executes post async.
    /// </summary>
    public Task<PostSingleWorkOrderResponse> PostAsync(string rawJsonBody, CancellationToken ct)
    {
        //  RunContext ctor does NOT have a parameter named 'TriggeredAtUtc'.
        // Use positional ctor args to match  actual record/ctor signature.
        var ctx = new RunContext(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            "Unknown",
            string.Empty);

        return PostAsync(ctx, rawJsonBody, ct);
    }

    /// <summary>
    /// Executes post async.
    /// </summary>
    public async Task<PostSingleWorkOrderResponse> PostAsync(RunContext context, string rawJsonBody, CancellationToken ct)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        // Prefer unified FSCM host; fall back to legacy endpoint base URL if provided.
        // If  set Endpoints:BaseUrl,  can omit Endpoints:SingleWorkOrderBaseUrlOverride.
        var baseUrl = ResolveBaseUrl(_opt.SingleWorkOrderBaseUrlOverride, legacyName: "SingleWorkOrderBaseUrlOverride");

        if (string.IsNullOrWhiteSpace(_opt.SingleWorkOrderPath))
            return new PostSingleWorkOrderResponse(false, 500, "{\"error\":\"Configuration missing: Endpoints:SingleWorkOrderPath\"}");

        var url = BuildUrl(baseUrl, _opt.SingleWorkOrderPath);

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Correlation propagation (best-effort)
        if (!string.IsNullOrWhiteSpace(context.RunId))
            req.Headers.TryAddWithoutValidation("x-run-id", context.RunId);

        if (!string.IsNullOrWhiteSpace(context.CorrelationId))
            req.Headers.TryAddWithoutValidation("x-correlation-id", context.CorrelationId);

        var payload = rawJsonBody ?? string.Empty;
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        _logger.LogInformation(
            "Calling FSCM single work order posting. Url={Url} RunId={RunId} CorrelationId={CorrelationId} PayloadBytes={Bytes}",
            url, context.RunId, context.CorrelationId, Encoding.UTF8.GetByteCount(payload));

        var sw = Stopwatch.StartNew();
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        var body = (await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false)) ?? string.Empty;
        sw.Stop();

        _logger.LogInformation(
            "FSCM single work order posting completed. Status={Status} Url={Url} RunId={RunId} CorrelationId={CorrelationId} ElapsedMs={ElapsedMs} ResponseBytes={ResponseBytes}",
            (int)resp.StatusCode, url, context.RunId, context.CorrelationId, sw.ElapsedMilliseconds, Encoding.UTF8.GetByteCount(body));

        // Auth failures => fail-fast
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogError(
                "Auth failure calling FSCM single work order posting. Status={Status} RunId={RunId} CorrelationId={CorrelationId}. Body={Body}",
                (int)resp.StatusCode, context.RunId, context.CorrelationId, Trim(body));

            throw new UnauthorizedAccessException(
                $"FSCM single work order posting unauthorized/forbidden. HTTP {(int)resp.StatusCode}. Body: {Trim(body)}");
        }

        // Transient => durable retry
        if ((int)resp.StatusCode == 429 || (int)resp.StatusCode >= 500)
        {
            _logger.LogWarning(
                "Transient failure calling FSCM single work order posting. Status={Status} RunId={RunId} CorrelationId={CorrelationId}. Body={Body}",
                (int)resp.StatusCode, context.RunId, context.CorrelationId, Trim(body));

            throw new HttpRequestException(
                $"Transient FSCM single work order posting failure {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {Trim(body)}",
                null,
                resp.StatusCode);
        }

        // Other 4xx => non-transient => return non-success
        if ((int)resp.StatusCode >= 400 && (int)resp.StatusCode <= 499)
        {
            _logger.LogWarning(
                "Non-transient failure calling FSCM single work order posting. Status={Status} RunId={RunId} CorrelationId={CorrelationId}. Body={Body}",
                (int)resp.StatusCode, context.RunId, context.CorrelationId, Trim(body));

            return new PostSingleWorkOrderResponse(false, (int)resp.StatusCode, body);
        }

        // Success
        return new PostSingleWorkOrderResponse(resp.IsSuccessStatusCode, (int)resp.StatusCode, body);
    }

    // ---------------------------
    // FSCM host selection
    // ---------------------------
    /// <summary>
    /// Executes resolve base url.
    /// </summary>
    private string ResolveBaseUrl(string? legacyBaseUrl, string legacyName)
    {
        // Preferred: unified FSCM host
        if (!string.IsNullOrWhiteSpace(_opt.BaseUrl))
            return _opt.BaseUrl.TrimEnd('/');

        // Fallback: legacy per-endpoint base URL
        if (!string.IsNullOrWhiteSpace(legacyBaseUrl))
            return legacyBaseUrl.TrimEnd('/');

        // If neither exists, return a deterministic config error response upstream.
        throw new InvalidOperationException(
            $"FSCM base URL is not configured. Set 'Fscm:BaseUrl' (preferred) or legacy 'Endpoints:{legacyName}'.");
    }

    /// <summary>
    /// Executes build url.
    /// </summary>
    private static string BuildUrl(string baseUrl, string path)
    {
        var b = baseUrl.TrimEnd('/');
        var p = path.StartsWith('/') ? path : "/" + path;
        return b + p;
    }

    /// <summary>
    /// Executes trim.
    /// </summary>
    private static string Trim(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        const int max = 4000;
        return string.Concat(s.AsSpan(0, max), " ...");

    }
}
