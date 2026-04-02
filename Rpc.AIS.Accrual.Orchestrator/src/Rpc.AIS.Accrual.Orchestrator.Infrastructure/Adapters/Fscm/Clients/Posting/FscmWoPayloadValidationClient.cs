using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Resilience;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;

public sealed partial class FscmWoPayloadValidationClient
    : Rpc.AIS.Accrual.Orchestrator.Core.Abstractions.IFscmWoPayloadValidationClient
{
    private readonly HttpClient _http;
    private readonly FscmOptions _endpoints;
    private readonly IFscmPostRequestFactory _reqFactory;
    private readonly IResilientHttpExecutor _executor;
    private readonly ILogger<FscmWoPayloadValidationClient> _logger;
    private readonly IAisLogger _aisLogger;
    private readonly IAisDiagnosticsOptions _diag;

    public FscmWoPayloadValidationClient(
        HttpClient http,
        FscmOptions endpoints,
        IFscmPostRequestFactory reqFactory,
        IResilientHttpExecutor executor,
        IAisLogger aisLogger,
        IAisDiagnosticsOptions diag,
        ILogger<FscmWoPayloadValidationClient> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
        _reqFactory = reqFactory ?? throw new ArgumentNullException(nameof(reqFactory));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _aisLogger = aisLogger ?? throw new ArgumentNullException(nameof(aisLogger));
        _diag = diag ?? throw new ArgumentNullException(nameof(diag));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Rpc.AIS.Accrual.Orchestrator.Core.Abstractions.RemoteWoPayloadValidationResult> ValidateAsync(
        RunContext ctx,
        JournalType journalType,
        string normalizedWoPayloadJson,
        CancellationToken ct)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));

        if (string.IsNullOrWhiteSpace(normalizedWoPayloadJson))
        {
            return new Rpc.AIS.Accrual.Orchestrator.Core.Abstractions.RemoteWoPayloadValidationResult(
                IsSuccessStatusCode: true,
                StatusCode: 204,
                FilteredPayloadJson: "{}",
                Failures: Array.Empty<WoPayloadValidationFailure>(),
                RawResponse: null,
                ElapsedMs: 0,
                Url: string.Empty);
        }

        var baseUrl = _endpoints.ResolveBaseUrl(_endpoints.BaseUrl);
        if (string.IsNullOrWhiteSpace(_endpoints.JournalValidatePath))
        {
            _logger.LogInformation(
                "FSCM journal validate path not configured. Skipping remote validation.");

            return new Rpc.AIS.Accrual.Orchestrator.Core.Abstractions.RemoteWoPayloadValidationResult(
                IsSuccessStatusCode: true,
                StatusCode: (int)HttpStatusCode.OK,
                FilteredPayloadJson: normalizedWoPayloadJson,
                Failures: Array.Empty<WoPayloadValidationFailure>(),
                RawResponse: null,
                ElapsedMs: 0,
                Url: string.Empty
            );
        }

        var url = CombineUrl(baseUrl, _endpoints.JournalValidatePath);

        var (woGuid, woId) = TryGetFirstWorkOrderIdentity(normalizedWoPayloadJson);

        await _aisLogger.LogJsonPayloadAsync(
            runId: ctx.RunId,
            step: "FSCM_WO_PAYLOAD_VALIDATE",
            message: "Outbound payload to FSCM validation",
            payloadType: "FSCM_VALIDATE_REQUEST",
            workOrderGuid: woGuid,
            workOrderNumber: woId,
            json: normalizedWoPayloadJson,
            logBody: _diag.LogPayloadBodies && (_diag.LogMultiWoPayloadBody || !string.Equals(woGuid, "MULTI", StringComparison.OrdinalIgnoreCase)),
            snippetChars: _diag.PayloadSnippetChars,
            chunkChars: _diag.PayloadChunkChars,
            ct: ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Calling FSCM custom validation. JournalType={JournalType} Url={Url} RunId={RunId} CorrelationId={CorrelationId}",
            journalType, url, ctx.RunId, ctx.CorrelationId);

        var sw = Stopwatch.StartNew();

        HttpResponseMessage response;

        try
        {
            response = await _executor.SendAsync(
                    http: _http,
                    requestFactory: () => _reqFactory.CreateJsonPost(ctx, url, normalizedWoPayloadJson),
                    ctx: ctx,
                    operationName: "FSCM_WO_PAYLOAD_VALIDATE",
                    ct: ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            sw.Stop();

            _logger.LogError(ex,
                "FSCM validation call threw exception. Fail-closed. Url={Url}",
                url);

            return new Rpc.AIS.Accrual.Orchestrator.Core.Abstractions.RemoteWoPayloadValidationResult(
                IsSuccessStatusCode: false,
                StatusCode: 500,
                FilteredPayloadJson: normalizedWoPayloadJson,
                Failures: Array.Empty<WoPayloadValidationFailure>(),
                RawResponse: ex.Message,
                ElapsedMs: sw.ElapsedMilliseconds,
                Url: url);
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        var statusCode = (int)response.StatusCode;
        var forceBodyLog = statusCode >= 500;

        await _aisLogger.LogJsonPayloadAsync(
            runId: ctx.RunId,
            step: "FSCM_WO_PAYLOAD_VALIDATE",
            message: "Inbound response from FSCM validation",
            payloadType: "FSCM_VALIDATE_RESPONSE",
            workOrderGuid: woGuid,
            workOrderNumber: woId,
            json: body ?? string.Empty,
            logBody: _diag.LogPayloadBodies || forceBodyLog,
            snippetChars: _diag.PayloadSnippetChars,
            chunkChars: _diag.PayloadChunkChars,
            ct: ct).ConfigureAwait(false);

        sw.Stop();

        var ok = statusCode >= 200 && statusCode <= 299;

        if (!ok)
        {
            _logger.LogWarning(
                "FSCM validation failed. StatusCode={StatusCode} ElapsedMs={ElapsedMs} Url={Url} BodySnippet={BodySnippet}",
                statusCode, sw.ElapsedMilliseconds, url, SafeSnippet(body));

            if (statusCode >= 500)
            {
                _logger.LogError(
                    "FSCM validation 5xx detail. StatusCode={StatusCode} Url={Url} RunId={RunId} CorrelationId={CorrelationId} Body={Body}",
                    statusCode,
                    url,
                    ctx.RunId,
                    ctx.CorrelationId,
                    LogText.TrimForLog(body ?? string.Empty));
            }

            return new Rpc.AIS.Accrual.Orchestrator.Core.Abstractions.RemoteWoPayloadValidationResult(
                IsSuccessStatusCode: false,
                StatusCode: statusCode,
                FilteredPayloadJson: normalizedWoPayloadJson,
                Failures: Array.Empty<WoPayloadValidationFailure>(),
                RawResponse: body,
                ElapsedMs: sw.ElapsedMilliseconds,
                Url: url);
        }

        try
        {
            var parsed = ParseValidationResponse(body, normalizedWoPayloadJson);

            return new Rpc.AIS.Accrual.Orchestrator.Core.Abstractions.RemoteWoPayloadValidationResult(
                IsSuccessStatusCode: true,
                StatusCode: statusCode,
                FilteredPayloadJson: parsed.FilteredPayloadJson,
                Failures: parsed.Failures,
                RawResponse: body,
                ElapsedMs: sw.ElapsedMilliseconds,
                Url: url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "FSCM validation response parsing failed. Fail-closed. Url={Url}",
                url);

            return new Rpc.AIS.Accrual.Orchestrator.Core.Abstractions.RemoteWoPayloadValidationResult(
                IsSuccessStatusCode: false,
                StatusCode: 500,
                FilteredPayloadJson: normalizedWoPayloadJson,
                Failures: Array.Empty<WoPayloadValidationFailure>(),
                RawResponse: body,
                ElapsedMs: sw.ElapsedMilliseconds,
                Url: url);
        }
    }

    private static string CombineUrl(string baseUrl, string path)
    {
        baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
        path = (path ?? string.Empty).TrimStart('/');
        return string.IsNullOrWhiteSpace(path) ? baseUrl : $"{baseUrl}/{path}";
    }

    private static string SafeSnippet(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        return body.Length <= 600 ? body : body.Substring(0, 600);
    }
}
