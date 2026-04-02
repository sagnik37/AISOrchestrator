using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Provides fscm baseline fetcher behavior.
/// </summary>
public sealed class FscmBaselineFetcher : Rpc.AIS.Accrual.Orchestrator.Core.Abstractions.IFscmBaselineFetcher
{
    private readonly HttpClient _http;
    private readonly IOptions<FscmOptions> _opt;
    private readonly ILogger<FscmBaselineFetcher> _log;

    public FscmBaselineFetcher(HttpClient http, IOptions<FscmOptions> opt, ILogger<FscmBaselineFetcher> log)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _opt = opt ?? throw new ArgumentNullException(nameof(opt));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Executes fetch baseline async.
    /// </summary>
    public async Task<IReadOnlyList<FscmBaselineRecord>> FetchBaselineAsync(CancellationToken ct)
    {
        var o = _opt.Value;
        if (!o.BaselineEnabled)
        {
            _log.LogInformation("FSCM baseline fetch disabled.");
            return Array.Empty<FscmBaselineRecord>();
        }

        if (string.IsNullOrWhiteSpace(o.BaselineODataBaseUrl))
            throw new InvalidOperationException("Fscm:Baseline:ODataBaseUrl (via FscmOptions) missing.");

        var baseUrl = o.BaselineODataBaseUrl.TrimEnd('/') + "/";
        var results = new List<FscmBaselineRecord>();

        foreach (var set in o.BaselineEntitySets ?? Array.Empty<string>())
        {
            var url = $"{baseUrl}{set}";
            if (!string.IsNullOrWhiteSpace(o.BaselineInProgressFilter))
                url += $"?$filter={Uri.EscapeDataString(o.BaselineInProgressFilter)}";

            _log.LogInformation("FSCM.Baseline.Fetch EntitySet={Set} Url={Url}", set, url);

            using var resp = await _http.GetAsync(url, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);

            _log.LogInformation("FSCM.Baseline.Fetch Response EntitySet={Set} Status={Status} Bytes={Bytes}",
                set, (int)resp.StatusCode, json.Length);

            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var row in arr.EnumerateArray())
            {
                // Placeholder: update WO field mapping once finalized.
                var woNumber = row.TryGetProperty("WorkOrderId", out var w) ? w.GetString() : null;
                if (string.IsNullOrWhiteSpace(woNumber)) continue;

                var rawRow = row.GetRawText();
                var hash = Sha256Hex(rawRow);

                results.Add(new FscmBaselineRecord(
                    WorkOrderNumber: woNumber!,
                    JournalType: JournalTypeFromSet(set),
                    LineKey: $"{set}:{hash[..12]}",
                    Hash: hash));
            }
        }

        _log.LogInformation("FSCM.Baseline.Fetch Completed Records={Count}", results.Count);
        return results;
    }

    private static string JournalTypeFromSet(string entitySet) =>
        entitySet.ToLowerInvariant().Contains("hour") ? "Hour" :
        entitySet.ToLowerInvariant().Contains("expense") ? "Expense" : "Item";

    /// <summary>
    /// Executes sha 256 hex.
    /// </summary>
    private static string Sha256Hex(string s)
    {
        using var sha = SHA256.Create();
        var b = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(b).ToLowerInvariant();
    }
}
