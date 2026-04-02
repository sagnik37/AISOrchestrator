using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Thin client for FSCM custom project status update endpoint.
/// </summary>
public sealed class FscmProjectStatusHttpClient : IFscmProjectStatusClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    private readonly HttpClient _http;
    private readonly FscmOptions _opt;
    private readonly ILogger<FscmProjectStatusHttpClient> _log;

    public FscmProjectStatusHttpClient(HttpClient http, IOptions<FscmOptions> opt, ILogger<FscmProjectStatusHttpClient> log)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _opt = opt?.Value ?? throw new ArgumentNullException(nameof(opt));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Preferred overload: update project status for a specific WO + SubProjectId using the agreed payload contract.
    /// </summary>
    public async Task<FscmProjectStatusUpdateResult> UpdateAsync(
        RunContext ctx,
        string company,
        string subProjectId,
        Guid workOrderGuid,
        string workOrderId,
        int status,
        CancellationToken ct)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        if (string.IsNullOrWhiteSpace(company)) throw new ArgumentException("Company is required.", nameof(company));
        if (string.IsNullOrWhiteSpace(subProjectId)) throw new ArgumentException("SubProjectId is required.", nameof(subProjectId));
        if (workOrderGuid == Guid.Empty) throw new ArgumentException("WorkOrderGuid is required.", nameof(workOrderGuid));
        if (string.IsNullOrWhiteSpace(workOrderId)) throw new ArgumentException("WorkOrderId is required.", nameof(workOrderId));
        if (status <= 0) throw new ArgumentOutOfRangeException(nameof(status), "Status must be a positive integer.");

        if (string.IsNullOrWhiteSpace(_opt.UpdateProjectStatusPath))
        {
            _log.LogWarning("FSCM UpdateProjectStatusPath is not configured. Skipping status update. RunId={RunId} CorrelationId={CorrelationId}", ctx.RunId, ctx.CorrelationId);
            return new FscmProjectStatusUpdateResult(true, 200, "");
        }

        var url = BuildUrl(_opt.BaseUrl, _opt.UpdateProjectStatusPath);

        var payload = new
        {
            _request = new
            {
                Company = company,
                SubProjectId = subProjectId,
                WorkOrderGUID = "{" + workOrderGuid.ToString("D").ToUpperInvariant() + "}",
                WorkOrderID = workOrderId,
                ProjectStatus = status
            },
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = Encoding.UTF8.GetByteCount(json);

        _log.LogInformation(
            "FSCM_PROJECT_STATUS START Url={Url} RunId={RunId} CorrelationId={CorrelationId} PayloadBytes={Bytes} Company={Company} SubProjectId={SubProjectId} WorkOrderGuid={WorkOrderGuid} WorkOrderId={WorkOrderId} Status={Status} Payload={Payload}",
            url, ctx.RunId, ctx.CorrelationId, bytes, company, subProjectId, workOrderGuid, workOrderId, status,json);

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
            "FSCM_PROJECT_STATUS END Status={Status} Url={Url} RunId={RunId} CorrelationId={CorrelationId} ElapsedMs={ElapsedMs} ResponseBytes={ResponseBytes}",
            (int)resp.StatusCode, url, ctx.RunId, ctx.CorrelationId, sw.ElapsedMilliseconds, Encoding.UTF8.GetByteCount(body ?? string.Empty));

        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException($"FSCM auth failure at project status update. HTTP {(int)resp.StatusCode}. Body: {LogText.TrimForLog(body)}");

        if (resp.StatusCode == (HttpStatusCode)429 || (int)resp.StatusCode >= 500)
            throw new HttpRequestException($"FSCM transient failure at project status update. HTTP {(int)resp.StatusCode}. Body: {LogText.TrimForLog(body)}", null, resp.StatusCode);

        var ok = (int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299;
        return new FscmProjectStatusUpdateResult(ok, (int)resp.StatusCode, body);
    }

    /// <summary>
    /// Legacy overload: preserved. Sends Company as empty string and SubProjectId as GUID string.
    ///  maps textual newStatus to numeric Status when possible.
    /// </summary>
    public Task<FscmProjectStatusUpdateResult> UpdateAsync(RunContext ctx, Guid subprojectId, string newStatus, CancellationToken ct)
        => UpdateAsync(ctx, company: string.Empty, subProjectId: subprojectId.ToString("D"), newStatus: newStatus, ct: ct);

    /// <summary>
    /// Legacy overload: older callers used a textual status ("Posted", "Cancelled", ...).
    /// This adapter maps known values to numeric codes:
    /// - Posted => 5
    /// - Cancel / Cancelled / Canceled => 6
    /// </summary>
    public Task<FscmProjectStatusUpdateResult> UpdateAsync(RunContext ctx, string company, string subProjectId, string newStatus, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(newStatus)) throw new ArgumentException("NewStatus is required.", nameof(newStatus));

        var status = newStatus.Trim().ToLowerInvariant() switch
        {
            "posted" => 5,
            "cancel" => 6,
            "cancelled" => 6,
            "canceled" => 6,
            "cancelled " => 6,
            _ => throw new InvalidOperationException($"Unsupported legacy status value '{newStatus}'. Use numeric status overload instead.")
        };

        // Legacy overload doesn't carry WO details; pass placeholders to keep the FSCM contract happy.
        // Callers should migrate to the numeric overload.
        return UpdateAsync(
            ctx,
            company: string.IsNullOrWhiteSpace(company) ? string.Empty : company,
            subProjectId: subProjectId,
            workOrderGuid: Guid.Empty,
            workOrderId: "UNKNOWN",
            status: status,
            ct: ct);
    }

    private static string BuildUrl(string baseUrl, string path)
        => $"{(baseUrl ?? string.Empty).TrimEnd('/')}/{(path ?? string.Empty).TrimStart('/')}";
}
