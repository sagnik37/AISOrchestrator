using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Resilience;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Utilities;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// FSCM OData client for AttributeTypeGlobalAttributes.
/// Loads mapping (Name, FSA) into memory so AIS can map FS attributes to FSCM attribute names at runtime.
/// </summary>
public sealed class FscmGlobalAttributeMappingHttpClient : IFscmGlobalAttributeMappingClient
{
    private readonly HttpClient _http;
    private readonly FscmOptions _opt;
    private readonly IResilientHttpExecutor _executor;
    private readonly ILogger<FscmGlobalAttributeMappingHttpClient> _logger;

    public FscmGlobalAttributeMappingHttpClient(
        HttpClient http,
        FscmOptions opt,
        IResilientHttpExecutor executor,
        ILogger<FscmGlobalAttributeMappingHttpClient> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _opt = opt ?? throw new ArgumentNullException(nameof(opt));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyDictionary<string, string>> GetFsToFscmNameMapAsync(RunContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var baseUrl = FscmUrlBuilder.ResolveFscmBaseUrl(_opt, legacyBaseUrl: null, legacyName: "FscmBaseUrl");
        var entity = string.IsNullOrWhiteSpace(_opt.AttributeTypeGlobalAttributesEntitySet)
            ? "AttributeTypeGlobalAttributes"
            : _opt.AttributeTypeGlobalAttributesEntitySet;

        var url = FscmUrlBuilder.BuildUrl(baseUrl, $"/data/{entity}");
        var full = $"{url}?$select=Name,FSASchemaName";

        var sw = Stopwatch.StartNew();
        _logger.LogInformation("FSCM AttributeTypeGlobalAttributes fetch: GET {Url} (runId={RunId}, corrId={CorrId})",
            full, ctx.RunId, ctx.CorrelationId);

        var resp = await _executor.SendAsync(
            _http,
            () => new HttpRequestMessage(HttpMethod.Get, full),
            ctx,
            operationName: "FSCM_ATTRIBUTE_TYPE_GLOBAL_ATTRIBUTES",
            ct).ConfigureAwait(false);

        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("FSCM AttributeTypeGlobalAttributes non-success: status={StatusCode} bodyLen={Len} elapsedMs={ElapsedMs}",
                (int)resp.StatusCode, body?.Length ?? 0, sw.ElapsedMilliseconds);

            // Fail-safe: empty => caller may fall back to config mapping.
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("FSCM AttributeTypeGlobalAttributes response missing 'value' array. elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);
            return map;
        }

        foreach (var row in arr.EnumerateArray())
        {
            if (!row.TryGetProperty("FSASchemaName", out var fsaEl) || fsaEl.ValueKind != JsonValueKind.String)
                continue;
            if (!row.TryGetProperty("Name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                continue;

            var fsaKey = (fsaEl.GetString() ?? string.Empty).Trim();
            var fscmName = (nameEl.GetString() ?? string.Empty).Trim();

            if (fsaKey.Length == 0 || fscmName.Length == 0)
                continue;

            map[fsaKey] = fscmName;
        }

        _logger.LogInformation("FSCM AttributeTypeGlobalAttributes mapping loaded: count={Count} elapsedMs={ElapsedMs}",
            map.Count, sw.ElapsedMilliseconds);

        return map;
    }
}
