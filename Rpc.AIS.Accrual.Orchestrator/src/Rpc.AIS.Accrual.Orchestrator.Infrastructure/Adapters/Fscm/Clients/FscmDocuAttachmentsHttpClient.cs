using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Thin client for FSCM OData custom entity RPCCustomDocuAttachments.
/// </summary>
public sealed class FscmDocuAttachmentsHttpClient : IFscmDocuAttachmentsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    private readonly HttpClient _http;
    private readonly FscmOptions _opt;
    private readonly ILogger<FscmDocuAttachmentsHttpClient> _log;

    public FscmDocuAttachmentsHttpClient(HttpClient http, IOptions<FscmOptions> opt, ILogger<FscmDocuAttachmentsHttpClient> log)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _opt = opt?.Value ?? throw new ArgumentNullException(nameof(opt));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<FscmDocuAttachmentUpsertResult> UpsertAsync(RunContext ctx, FscmDocuAttachmentUpsertRequest request, CancellationToken ct)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        if (request is null) throw new ArgumentNullException(nameof(request));

        var path = string.IsNullOrWhiteSpace(_opt.DocuAttachmentsPath)
            ? "/data/RPCCustomDocuAttachments"
            : _opt.DocuAttachmentsPath;

        var url = $"{_opt.BaseUrl.TrimEnd('/')}/{_opt.DocuAttachmentsPath.TrimStart('/')}";

        var json = JsonSerializer.Serialize(new
        {
            dataAreaId = request.DataAreaId,
            FileTypeId = request.FileTypeId,
            FileName = request.FileName,
            TableName = request.TableName,
            PrimaryRefNumber = request.PrimaryRefNumber,
            FileURL = request.FileUrl,
            Restriction = request.Restriction,
            SecondaryRefNumber = request.SecondaryRefNumber,
            Name = request.Name,
            Notes = request.Notes,
            Author = request.Author
        }, JsonOptions);

        _log.LogInformation(
            "FSCM_DOCU_ATTACH START Url={Url} RunId={RunId} CorrelationId={CorrelationId} Company={Company} PrimaryRef={PrimaryRef} FileName={FileName} Payload={Payload}",
            url, ctx.RunId, ctx.CorrelationId, request.DataAreaId, request.PrimaryRefNumber, request.FileName,json);
        
        var sw = Stopwatch.StartNew();
        using var msg = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(ctx.RunId)) msg.Headers.TryAddWithoutValidation("x-run-id", ctx.RunId);
        if (!string.IsNullOrWhiteSpace(ctx.CorrelationId)) msg.Headers.TryAddWithoutValidation("x-correlation-id", ctx.CorrelationId);

        using var resp = await _http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        var body = resp.Content is null ? string.Empty : await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        sw.Stop();

        _log.LogInformation(
            "FSCM_DOCU_ATTACH END Status={Status} Url={Url} RunId={RunId} CorrelationId={CorrelationId} ElapsedMs={ElapsedMs} ResponseBytes={Bytes}",
            (int)resp.StatusCode, url, ctx.RunId, ctx.CorrelationId, sw.ElapsedMilliseconds, Encoding.UTF8.GetByteCount(body ?? string.Empty));

        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException($"FSCM auth failure at DocuAttachments upsert. HTTP {(int)resp.StatusCode}. Body: {LogText.TrimForLog(body)}");

        if (resp.StatusCode == (HttpStatusCode)429 || (int)resp.StatusCode >= 500)
            throw new HttpRequestException($"FSCM transient failure at DocuAttachments upsert. HTTP {(int)resp.StatusCode}. Body: {LogText.TrimForLog(body)}", null, resp.StatusCode);

        var ok = (int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299;
        return new FscmDocuAttachmentUpsertResult(ok, (int)resp.StatusCode, body);
    }
}
