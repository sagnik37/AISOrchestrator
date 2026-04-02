using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services;

/// <summary>
/// Provides delta comparer behavior.
/// </summary>
public sealed class DeltaComparer
{
    private readonly ILogger<DeltaComparer> _log;

    public DeltaComparer(ILogger<DeltaComparer> log)
    {
        _log = log;
    }

    public IReadOnlyList<DeltaCompareResult> Compare(
        string newPayloadJson,
        string? baselineJson)
    {
        if (string.IsNullOrWhiteSpace(baselineJson))
        {
            _log.LogInformation("Baseline empty. Treating all records as new.");
            return Array.Empty<DeltaCompareResult>();
        }

        using var newDoc = JsonDocument.Parse(newPayloadJson);
        using var oldDoc = JsonDocument.Parse(baselineJson);

        var newWos = ExtractWoKeys(newDoc);
        var oldWos = ExtractWoKeys(oldDoc);

        var results = new List<DeltaCompareResult>();

        foreach (var wo in newWos)
        {
            oldWos.TryGetValue(wo.Key, out var oldCount);

            results.Add(new DeltaCompareResult(
                WorkOrderNumber: wo.Key,
                JournalType: "Mixed",
                AddedOrChanged: Math.Max(0, wo.Value - oldCount),
                Removed: Math.Max(0, oldCount - wo.Value)));
        }

        return results;
    }

    /// <summary>
    /// Executes extract wo keys.
    /// </summary>
    private static Dictionary<string, int> ExtractWoKeys(JsonDocument doc)
    {
        var dict = new Dictionary<string, int>();

        if (!doc.RootElement.TryGetProperty("_request", out var r)) return dict;
        if (!r.TryGetProperty("WOList", out var arr)) return dict;

        foreach (var wo in arr.EnumerateArray())
        {
            if (!wo.TryGetProperty("Work order ID", out var id)) continue;

            var key = id.GetString()!;
            var count = wo.EnumerateObject().Count();

            dict[key] = count;
        }

        return dict;
    }
}

/// <summary>
/// Carries delta compare result data.
/// </summary>
public sealed record DeltaCompareResult(
    string WorkOrderNumber,
    string JournalType,
    int AddedOrChanged,
    int Removed);
