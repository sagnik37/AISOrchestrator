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

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

public sealed class InvoiceAttributesHttpTransport : IInvoiceAttributesHttpTransport
{
    private readonly IAisLogger _aisLogger;
    private readonly IAisDiagnosticsOptions _diag;
    private readonly ILogger<InvoiceAttributesHttpTransport> _log;

    public InvoiceAttributesHttpTransport(
        IAisLogger aisLogger,
        IAisDiagnosticsOptions diag,
        ILogger<InvoiceAttributesHttpTransport> log)
    {
        _aisLogger = aisLogger ?? throw new ArgumentNullException(nameof(aisLogger));
        _diag = diag ?? throw new ArgumentNullException(nameof(diag));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<InvoiceAttributesTransportResult> PostJsonAsync(
        HttpClient http,
        RunContext ctx,
        string url,
        string payloadJson,
        string operation,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(ctx);

        var payloadBytes = Encoding.UTF8.GetByteCount(payloadJson ?? string.Empty);
        var (woGuid, woId) = TryGetFirstWorkOrderIdentity(payloadJson ?? string.Empty);

        await _aisLogger.LogJsonPayloadAsync(
            runId: ctx.RunId,
            step: operation,
            message: "Outbound payload to FSCM invoice attributes endpoint",
            payloadType: "FSCM_INV_ATTR_REQUEST",
            workOrderGuid: woGuid,
            workOrderNumber: woId,
            json: payloadJson ?? string.Empty,
            logBody: _diag.LogPayloadBodies && (_diag.LogMultiWoPayloadBody || !string.Equals(woGuid, "MULTI", StringComparison.OrdinalIgnoreCase)),
            snippetChars: _diag.PayloadSnippetChars,
            chunkChars: _diag.PayloadChunkChars,
            ct: ct).ConfigureAwait(false);

        _log.LogInformation(
            "{Op} START Url={Url} RunId={RunId} CorrelationId={CorrelationId} PayloadBytes={Bytes}",
            operation, url, ctx.RunId, ctx.CorrelationId, payloadBytes);

        var sw = Stopwatch.StartNew();

        using var msg = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payloadJson ?? string.Empty, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(ctx.RunId))
            msg.Headers.TryAddWithoutValidation("x-run-id", ctx.RunId);

        if (!string.IsNullOrWhiteSpace(ctx.CorrelationId))
            msg.Headers.TryAddWithoutValidation("x-correlation-id", ctx.CorrelationId);

        using var resp = await http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        var body = resp.Content is null ? string.Empty : await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        var forceBodyLog = (int)resp.StatusCode >= 500;

        await _aisLogger.LogJsonPayloadAsync(
            runId: ctx.RunId,
            step: operation,
            message: "Inbound response from FSCM invoice attributes endpoint",
            payloadType: "FSCM_INV_ATTR_RESPONSE",
            workOrderGuid: woGuid,
            workOrderNumber: woId,
            json: body ?? string.Empty,
            logBody: _diag.LogPayloadBodies || forceBodyLog,
            snippetChars: _diag.PayloadSnippetChars,
            chunkChars: _diag.PayloadChunkChars,
            ct: ct).ConfigureAwait(false);

        sw.Stop();

        _log.LogInformation(
            "{Op} END Status={Status} Url={Url} RunId={RunId} CorrelationId={CorrelationId} ElapsedMs={ElapsedMs} ResponseBytes={ResponseBytes}",
            operation, (int)resp.StatusCode, url, ctx.RunId, ctx.CorrelationId, sw.ElapsedMilliseconds, Encoding.UTF8.GetByteCount(body ?? string.Empty));

        if ((int)resp.StatusCode >= 500)
        {
            _log.LogError(
                "FSCM 5xx response. Op={Op} Status={Status} Url={Url} RunId={RunId} CorrelationId={CorrelationId} Body={Body}",
                operation,
                (int)resp.StatusCode,
                url,
                ctx.RunId,
                ctx.CorrelationId,
                LogText.TrimForLog(body ?? string.Empty));
        }

        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException($"FSCM auth failure at {operation}. HTTP {(int)resp.StatusCode}. Body: {LogText.TrimForLog(body)}");

        if (resp.StatusCode == (HttpStatusCode)429 || (int)resp.StatusCode >= 500)
            throw new HttpRequestException($"FSCM transient failure at {operation}. HTTP {(int)resp.StatusCode}. Body: {LogText.TrimForLog(body)}", null, resp.StatusCode);

        return new InvoiceAttributesTransportResult(resp.StatusCode, body ?? string.Empty, sw.ElapsedMilliseconds);
    }

    private static (string WorkOrderGuid, string? WorkOrderId) TryGetFirstWorkOrderIdentity(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("_request", out var req) || req.ValueKind != JsonValueKind.Object)
                return ("MULTI", null);
            if (!req.TryGetProperty("WOList", out var list) || list.ValueKind != JsonValueKind.Array)
                return ("MULTI", null);

            using var e = list.EnumerateArray();
            if (!e.MoveNext())
                return ("MULTI", null);

            var wo = e.Current;

            static string? ReadString(JsonElement obj, string prop)
            {
                if (obj.ValueKind != JsonValueKind.Object)
                    return null;
                if (obj.TryGetProperty(prop, out var v))
                {
                    if (v.ValueKind == JsonValueKind.String) return v.GetString();
                    if (v.ValueKind == JsonValueKind.Number || v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) return v.ToString();
                }
                return null;
            }

            var guid = ReadString(wo, "WorkOrderGUID") ?? ReadString(wo, "WorkOrderGuid") ?? ReadString(wo, "workOrderGuid");
            var id = ReadString(wo, "WorkOrderID") ?? ReadString(wo, "WorkOrderId") ?? ReadString(wo, "workOrderId") ?? ReadString(wo, "WONumber");
            if (string.IsNullOrWhiteSpace(guid)) return ("MULTI", id);
            return (guid.Trim(), id);
        }
        catch
        {
            return ("MULTI", null);
        }
    }
}
