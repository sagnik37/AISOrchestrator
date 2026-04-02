using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Generic (no business IDs) warm-up endpoint to prime Dataverse + FSCM auth/TLS/DNS/HttpClient pipelines.
/// Intentionally uses small GET/HEAD probes and avoids executing any business operations.
/// </summary>
public sealed class WarmupReadUseCase : IWarmupReadUseCase
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly ILogger<WarmupReadUseCase> _log;
    private readonly IHttpClientFactory _http;
    private readonly WarmupOptions _opt;
    private readonly FscmOptions _fscm;

    public WarmupReadUseCase(
        ILogger<WarmupReadUseCase> log,
        IHttpClientFactory http,
        IOptions<WarmupOptions> opt,
        IOptions<FscmOptions> fscm)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _opt = opt?.Value ?? throw new ArgumentNullException(nameof(opt));
        _fscm = fscm?.Value ?? throw new ArgumentNullException(nameof(fscm));
    }

    public async Task<HttpResponseData> ExecuteAsync(HttpRequestData req, FunctionContext ctx)
    {
        if (!_opt.Enabled)
        {
            var disabled = req.CreateResponse(HttpStatusCode.NotFound);
            await disabled.WriteStringAsync("Warmup endpoint is disabled.");
            return disabled;
        }

        WarmupRequest body;
        try
        {
            body = await ReadBodyAsync(req, ctx.CancellationToken).ConfigureAwait(false)
                ?? new WarmupRequest(null, null, null);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Warmup request body parse failed.");
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("Invalid JSON request body.");
            return bad;
        }

        var minutes = Clamp(body.Minutes ?? _opt.DefaultMinutes, 1, _opt.MaxMinutes);
        var maxConcurrency = Clamp(body.MaxConcurrency ?? _opt.DefaultMaxConcurrency, 1, 4);
        var delayMs = Clamp(body.DelayMs ?? _opt.DefaultDelayMs, 0, 5000);

        var runId = Guid.NewGuid().ToString("D");
        var acceptedAt = DateTimeOffset.UtcNow;

        using var scope = LogScopes.BeginFunctionScope(_log, new LogScopeContext
        {
            Function = "WarmupRead",
            Operation = "WarmupRead",
            FlowName = "WarmupRead",
            Trigger = "Http",
            TriggerName = "WarmupRead",
            TriggerChannel = "Http",
            InitiatedBy = "Timer",
            RunId = runId,
            CorrelationId = runId,
            SourceSystem = "Timer",
            Stage = "Accepted"
        });

        _log.LogInformation(
            "Warmup ACCEPTED. RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} InitiatedBy={InitiatedBy} Stage={Stage} Minutes={Minutes} MaxConcurrency={MaxConcurrency} DelayMs={DelayMs}",
            runId, runId, "Timer", "WarmupRead", "Timer", "Accepted", minutes, maxConcurrency, delayMs);

        // 🔥 Run in background
        _ = Task.Run(async () =>
        {
            try
            {
                await RunWarmupAsync(runId, minutes, maxConcurrency, delayMs);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "WARMUP_BACKGROUND_FATAL RunId={RunId}", runId);
            }
        });

        var accepted = req.CreateResponse(HttpStatusCode.Accepted);
        await accepted.WriteStringAsync(JsonSerializer.Serialize(new
        {
            message = "Warmup accepted",
            runId,
            acceptedAtUtc = acceptedAt,
            minutes,
            maxConcurrency,
            delayMs
        }, JsonOpts));

        return accepted;
    }

    private async Task RunWarmupAsync(
    string runId,
    int minutes,
    int maxConcurrency,
    int delayMs)
    {
        var started = DateTimeOffset.UtcNow;
        var stopAt = started.AddMinutes(minutes);

        var dataverse = _http.CreateClient("warmup-dataverse");
        var fscm = _http.CreateClient("warmup-fscm");

        var operations = BuildOperations(dataverse, fscm, _opt, _fscm);

        var callsByOp = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var failuresByStatus = new Dictionary<int, int>();
        var failuresByOperation = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var loops = 0;
        var totalCalls = 0;
        long totalLatencyMs = 0;

        using var gate = new SemaphoreSlim(maxConcurrency);

        _log.LogInformation(
            "WARMUP_START RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} InitiatedBy={InitiatedBy} Stage={Stage} StartedUtc={StartedUtc} StopAtUtc={StopAtUtc}",
            runId, runId, "Timer", "WarmupRead", "Timer", "Discovery.Begin", started, stopAt);

        try
        {
            while (DateTimeOffset.UtcNow < stopAt)
            {
                loops++;

                var loopStarted = DateTimeOffset.UtcNow;

                var tasks = operations
                    .Select(op => RunBoundedAsync(
                        gate,
                        () => ProbeAsync(op, CancellationToken.None),
                        CancellationToken.None))
                    .ToArray();

                var results = await Task.WhenAll(tasks);

                var loopFailures = 0;
                long loopLatencyMs = 0;

                foreach (var rst in results)
                {
                    totalCalls++;
                    loopLatencyMs += rst.DurationMs;
                    totalLatencyMs += rst.DurationMs;

                    Increment(callsByOp, rst.Operation, 1);

                    if (!rst.IsSuccess)
                    {
                        loopFailures++;
                        Increment(failuresByStatus, rst.StatusCode, 1);
                        Increment(failuresByOperation, rst.Operation, 1);

                        _log.LogWarning(
                            "WARMUP_PROBE_FAIL RunId={RunId} Operation={Operation} Status={StatusCode}",
                            runId, rst.Operation, rst.StatusCode);
                    }
                }

                var loopEnded = DateTimeOffset.UtcNow;

                _log.LogInformation(
                    "WARMUP_LOOP_END RunId={RunId} Loop={Loop} DurationMs={DurationMs} Failures={Failures}",
                    runId,
                    loops,
                    (loopEnded - loopStarted).TotalMilliseconds,
                    loopFailures);

                if (delayMs > 0 && DateTimeOffset.UtcNow < stopAt)
                {
                    await Task.Delay(delayMs);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "WARMUP_FAILED RunId={RunId}", runId);
            throw;
        }
        finally
        {
            var ended = DateTimeOffset.UtcNow;

            _log.LogInformation(
                "WARMUP_END RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} Outcome={Outcome} DurationMs={DurationMs} Loops={Loops} Calls={Calls}",
                runId,
                runId,
                "Timer",
                "WarmupRead",
                "Success",
                (ended - started).TotalMilliseconds,
                loops,
                totalCalls);
        }
    }

    private static async Task<WarmupRequest?> ReadBodyAsync(HttpRequestData req, CancellationToken ct)
    {
        using var sr = new StreamReader(req.Body);
        var raw = await sr.ReadToEndAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return JsonSerializer.Deserialize<WarmupRequest>(
            raw,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private static IReadOnlyList<WarmupOperation> BuildOperations(
        HttpClient dataverse,
        HttpClient fscm,
        WarmupOptions warmupOptions,
        FscmOptions fscmOptions)
    {
        var operations = new List<WarmupOperation>();

        foreach (var path in warmupOptions.DataversePaths ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                operations.Add(new WarmupOperation(
                    Operation: $"dataverse:{path}",
                    Client: dataverse,
                    Target: path,
                    Method: HttpMethod.Get,
                    Accept405AsSuccess: false));
            }
        }

        foreach (var path in warmupOptions.FscmPaths ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                operations.Add(new WarmupOperation(
                    Operation: $"fscm:{path}",
                    Client: fscm,
                    Target: path,
                    Method: HttpMethod.Get,
                    Accept405AsSuccess: false));
            }
        }

        foreach (var endpoint in BuildCustomFscmWarmupEndpoints(fscmOptions))
        {
            operations.Add(new WarmupOperation(
                Operation: $"fscm-custom:{endpoint.Name}",
                Client: fscm,
                Target: endpoint.Url,
                Method: HttpMethod.Head,
                Accept405AsSuccess: true));
        }

        return operations;
    }

    private static async Task<ProbeOutcome> ProbeAsync(WarmupOperation operation, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var msg = new HttpRequestMessage(operation.Method, operation.Target);
            using var resp = await operation.Client
                .SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            sw.Stop();

            var statusCode = (int)resp.StatusCode;
            var isSuccess =
                resp.IsSuccessStatusCode ||
                (operation.Accept405AsSuccess && statusCode == (int)HttpStatusCode.MethodNotAllowed);

            return new ProbeOutcome(
                Operation: operation.Operation,
                Target: operation.Target,
                Method: operation.Method.Method,
                IsSuccess: isSuccess,
                StatusCode: statusCode,
                DurationMs: sw.ElapsedMilliseconds,
                Error: null);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            sw.Stop();

            return new ProbeOutcome(
                Operation: operation.Operation,
                Target: operation.Target,
                Method: operation.Method.Method,
                IsSuccess: false,
                StatusCode: 408,
                DurationMs: sw.ElapsedMilliseconds,
                Error: ex.Message);
        }
        catch (OperationCanceledException ex)
        {
            sw.Stop();

            return new ProbeOutcome(
                Operation: operation.Operation,
                Target: operation.Target,
                Method: operation.Method.Method,
                IsSuccess: false,
                StatusCode: 499,
                DurationMs: sw.ElapsedMilliseconds,
                Error: ex.Message);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();

            return new ProbeOutcome(
                Operation: operation.Operation,
                Target: operation.Target,
                Method: operation.Method.Method,
                IsSuccess: false,
                StatusCode: 503,
                DurationMs: sw.ElapsedMilliseconds,
                Error: ex.Message);
        }
        catch (Exception ex)
        {
            sw.Stop();

            return new ProbeOutcome(
                Operation: operation.Operation,
                Target: operation.Target,
                Method: operation.Method.Method,
                IsSuccess: false,
                StatusCode: 500,
                DurationMs: sw.ElapsedMilliseconds,
                Error: ex.Message);
        }
    }

    private static IReadOnlyList<CustomWarmupEndpoint> BuildCustomFscmWarmupEndpoints(FscmOptions options)
    {
        if (options is null)
            return Array.Empty<CustomWarmupEndpoint>();

        var endpoints = new List<CustomWarmupEndpoint>();

        AddCustomEndpoint(
            endpoints,
            "subproject",
            options.ResolveBaseUrl(options.SubProjectBaseUrlOverride),
            options.SubProjectPath);

        AddCustomEndpoint(
            endpoints,
            "journal-validate",
            options.BaseUrl,
            options.JournalValidatePath);

        AddCustomEndpoint(
            endpoints,
            "journal-create",
            options.BaseUrl,
            options.JournalCreatePath);

        AddCustomEndpoint(
            endpoints,
            "journal-post",
            options.BaseUrl,
            options.JournalPostPath);

        AddCustomEndpoint(
            endpoints,
            "invoice-attributes-update",
            options.BaseUrl,
            options.UpdateInvoiceAttributesPath);

        AddCustomEndpoint(
            endpoints,
            "project-status-update",
            options.BaseUrl,
            options.UpdateProjectStatusPath);

        AddCustomEndpoint(
            endpoints,
            "wo-payload-validation",
            options.ResolveBaseUrl(options.WoPayloadValidationBaseUrlOverride),
            options.WoPayloadValidationPath);

        AddCustomEndpoint(
            endpoints,
            "single-workorder",
            options.ResolveBaseUrl(options.SingleWorkOrderBaseUrlOverride),
            options.SingleWorkOrderPath);

        AddCustomEndpoint(
            endpoints,
            "workorder-status-update",
            options.ResolveBaseUrl(options.WorkOrderStatusUpdateBaseUrlOverride),
            options.WorkOrderStatusUpdatePath);

        return endpoints;
    }

    private static void AddCustomEndpoint(
        ICollection<CustomWarmupEndpoint> endpoints,
        string name,
        string? baseUrl,
        string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(relativePath))
            return;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            return;

        if (!Uri.TryCreate(baseUri, relativePath, out var fullUri))
            return;

        var absoluteUrl = fullUri.ToString();

        if (endpoints.Any(x => string.Equals(x.Url, absoluteUrl, StringComparison.OrdinalIgnoreCase)))
            return;

        endpoints.Add(new CustomWarmupEndpoint(name, absoluteUrl));
    }

    private static async Task<T> RunBoundedAsync<T>(
        SemaphoreSlim gate,
        Func<Task<T>> operation,
        CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private static int Clamp(int value, int min, int max)
        => value < min ? min : (value > max ? max : value);

    private static void Increment<TKey>(IDictionary<TKey, int> map, TKey key, int by)
        where TKey : notnull
    {
        map[key] = map.TryGetValue(key, out var current)
            ? current + by
            : by;
    }

    private sealed record WarmupRequest(int? Minutes, int? MaxConcurrency, int? DelayMs);

    private sealed record WarmupResult(
        string RunId,
        DateTimeOffset StartedUtc,
        DateTimeOffset EndedUtc,
        int MinutesRequested,
        int Loops,
        int TotalCalls,
        IReadOnlyDictionary<string, int> CallsByOperation,
        IReadOnlyDictionary<int, int> FailuresByStatus,
        IReadOnlyDictionary<string, int> FailuresByOperation,
        long TotalProbeLatencyMs,
        long AverageProbeLatencyMs);

    private sealed record ProbeOutcome(
        string Operation,
        string Target,
        string Method,
        bool IsSuccess,
        int StatusCode,
        long DurationMs,
        string? Error);

    private sealed record CustomWarmupEndpoint(string Name, string Url);

    private sealed record WarmupOperation(
        string Operation,
        HttpClient Client,
        string Target,
        HttpMethod Method,
        bool Accept405AsSuccess);
}
