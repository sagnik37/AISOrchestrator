using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Utilities;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;

public sealed partial class FscmJournalPoster
{
    private async Task<HttpPostOutcome> CallStepAsync(
        RunContext ctx,
        string stepName,
        string baseUrl,
        string pathOrEntitySet,
        string payloadJson,
        CancellationToken ct,
        bool isOData = false)
    {
        if (string.IsNullOrWhiteSpace(pathOrEntitySet))
        {
            _logger.LogWarning("FSCM step skipped because endpoint is empty. Step={Step} RunId={RunId} CorrelationId={CorrelationId}",
                stepName, ctx.RunId, ctx.CorrelationId);
            return new HttpPostOutcome(HttpStatusCode.OK, "", 0, "");
        }

        var url = isOData
            ? FscmUrlBuilder.BuildUrl(baseUrl, $"/data/{pathOrEntitySet}")
            : FscmUrlBuilder.BuildUrl(baseUrl, pathOrEntitySet);

        _logger.LogInformation(
            "FSCM step START. Step={Step} Url={Url} RunId={RunId} CorrelationId={CorrelationId} PayloadBytes={Bytes}",
            stepName, url, ctx.RunId, ctx.CorrelationId, Encoding.UTF8.GetByteCount(payloadJson));

        var (woGuid, woId) = TryGetFirstWorkOrderIdentity(payloadJson, _logger, ctx);
        await LogOutboundPayloadAsync(ctx, stepName, woGuid, woId, payloadJson, ct).ConfigureAwait(false);

        var sw = Stopwatch.StartNew();
        HttpResponseMessage resp = await _executor.SendAsync(
            _http,
            () => _reqFactory.CreateJsonPost(ctx, url, payloadJson),
            ctx,
            operationName: stepName,
            ct).ConfigureAwait(false);

        var body = (await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false)) ?? string.Empty;
        sw.Stop();

        await LogInboundPayloadAsync(ctx, stepName, woGuid, woId, body, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "FSCM step END. Step={Step} Status={Status} Url={Url} RunId={RunId} CorrelationId={CorrelationId} ElapsedMs={ElapsedMs} ResponseBytes={ResponseBytes}",
            stepName, (int)resp.StatusCode, url, ctx.RunId, ctx.CorrelationId, sw.ElapsedMilliseconds, Encoding.UTF8.GetByteCount(body));

        ThrowIfAuthFailure(resp, body, stepName, ctx);
        return new HttpPostOutcome(resp.StatusCode, body, sw.ElapsedMilliseconds, url);
    }

    private Task LogOutboundPayloadAsync(
        RunContext ctx,
        string stepName,
        string woGuid,
        string? woId,
        string payloadJson,
        CancellationToken ct)
        => _aisLogger.LogJsonPayloadAsync(
            runId: ctx.RunId,
            step: stepName,
            message: "Outbound payload to FSCM",
            payloadType: "FSCM_REQUEST",
            workOrderGuid: woGuid,
            workOrderNumber: woId,
            json: payloadJson,
            logBody: _diag.LogPayloadBodies && (_diag.LogMultiWoPayloadBody || !string.Equals(woGuid, "MULTI", StringComparison.OrdinalIgnoreCase)),
            snippetChars: _diag.PayloadSnippetChars,
            chunkChars: _diag.PayloadChunkChars,
            ct: ct);

    private Task LogInboundPayloadAsync(
        RunContext ctx,
        string stepName,
        string woGuid,
        string? woId,
        string body,
        CancellationToken ct)
        => _aisLogger.LogJsonPayloadAsync(
            runId: ctx.RunId,
            step: stepName,
            message: "Inbound response from FSCM",
            payloadType: "FSCM_RESPONSE",
            workOrderGuid: woGuid,
            workOrderNumber: woId,
            json: body,
            logBody: _diag.LogPayloadBodies,
            snippetChars: _diag.PayloadSnippetChars,
            chunkChars: _diag.PayloadChunkChars,
            ct: ct);

    private void ThrowIfAuthFailure(HttpResponseMessage resp, string body, string stepName, RunContext ctx)
    {
        if (resp.StatusCode != HttpStatusCode.Unauthorized && resp.StatusCode != HttpStatusCode.Forbidden)
            return;

        _logger.LogError(
            "FSCM auth failure. Step={Step} Status={Status} RunId={RunId} CorrelationId={CorrelationId}. Body={Body}",
            stepName, (int)resp.StatusCode, ctx.RunId, ctx.CorrelationId, LogText.TrimForLog(body));

        throw new HttpRequestException(
            $"FSCM auth failed at {stepName} ({(int)resp.StatusCode} {resp.ReasonPhrase}). Body: {LogText.TrimForLog(body)}",
            null,
            resp.StatusCode);
    }

    private static (string WorkOrderGuid, string? WorkOrderId) TryGetFirstWorkOrderIdentity(string json, ILogger logger, RunContext ctx)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("_request", out var req) || req.ValueKind != System.Text.Json.JsonValueKind.Object)
                return ("MULTI", null);
            if (!req.TryGetProperty("WOList", out var list) || list.ValueKind != System.Text.Json.JsonValueKind.Array)
                return ("MULTI", null);

            using var e = list.EnumerateArray();
            if (!e.MoveNext()) return ("MULTI", null);
            var wo = e.Current;

            static string? ReadString(System.Text.Json.JsonElement obj, string prop)
            {
                if (obj.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
                if (obj.TryGetProperty(prop, out var v))
                {
                    if (v.ValueKind == System.Text.Json.JsonValueKind.String) return v.GetString();
                    if (v.ValueKind == System.Text.Json.JsonValueKind.Number || v.ValueKind == System.Text.Json.JsonValueKind.True || v.ValueKind == System.Text.Json.JsonValueKind.False) return v.ToString();
                }
                return null;
            }

            var guid = ReadString(wo, "WorkOrderGUID") ?? ReadString(wo, "WorkOrderGuid") ?? ReadString(wo, "workOrderGuid");
            var id = ReadString(wo, "WorkOrderID") ?? ReadString(wo, "WorkOrderId") ?? ReadString(wo, "workOrderId") ?? ReadString(wo, "WO Number");
            if (string.IsNullOrWhiteSpace(guid)) return ("MULTI", id);
            return (guid.Trim(), id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Unable to extract WorkOrder identity from payload JSON. RunId={RunId} CorrelationId={CorrelationId}.",
                ctx.RunId,
                ctx.CorrelationId);
            return ("MULTI", null);
        }
    }
}
