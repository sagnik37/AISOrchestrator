using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

/// <summary>
/// Throttled trace helper for static helpers that do not have an ILogger available.
/// </summary>
public static class ThrottledTrace
{
    private static readonly ConcurrentDictionary<string, int> Counts = new(StringComparer.Ordinal);
    public const int DefaultMaxOccurrences = 3;

    public static void Debug(string key, string message, Exception? ex = null, int maxOccurrences = DefaultMaxOccurrences)
    {
        if (string.IsNullOrWhiteSpace(key)) key = "<unknown>";
        var n = Counts.AddOrUpdate(key, 1, (_, cur) => cur + 1);
        if (n > maxOccurrences) return;

        if (ex is null)
            Trace.WriteLine($"[Tolerant] {message} Key={key} Occurrence={n}/{maxOccurrences}");
        else
            Trace.WriteLine($"[Tolerant] {message} Key={key} Occurrence={n}/{maxOccurrences} Ex={ex.GetType().Name}:{ex.Message}");
    }
}
