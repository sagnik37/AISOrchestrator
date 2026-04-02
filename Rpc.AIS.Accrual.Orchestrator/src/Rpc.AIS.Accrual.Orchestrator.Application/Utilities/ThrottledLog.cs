using System;
using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

/// <summary>
/// Throttled debug logging helper to make best-effort parsing/probing failures observable
/// without spamming logs.
/// </summary>
public static class ThrottledLog
{
    private static readonly ConcurrentDictionary<string, int> Counts = new(StringComparer.Ordinal);

    /// <summary>Default: log only the first 3 occurrences per key, per process.</summary>
    public const int DefaultMaxOccurrences = 3;

    public static void Debug(
        ILogger logger,
        RunContext? ctx,
        string key,
        string message,
        Exception? ex = null,
        int maxOccurrences = DefaultMaxOccurrences)
    {
        if (logger is null) return;
        if (string.IsNullOrWhiteSpace(key)) key = "<unknown>";

        var n = Counts.AddOrUpdate(key, 1, (_, cur) => cur + 1);
        if (n > maxOccurrences) return;
        if (!logger.IsEnabled(LogLevel.Debug)) return;

        var runId = ctx?.RunId;
        var correlationId = ctx?.CorrelationId;

        if (ex is null)
        {
            logger.LogDebug(
                "[Tolerant] {Message} Key={Key} Occurrence={Occurrence}/{Max} RunId={RunId} CorrelationId={CorrelationId}",
                message, key, n, maxOccurrences, runId, correlationId);
        }
        else
        {
            logger.LogDebug(
                ex,
                "[Tolerant] {Message} Key={Key} Occurrence={Occurrence}/{Max} RunId={RunId} CorrelationId={CorrelationId}",
                message, key, n, maxOccurrences, runId, correlationId);
        }
    }
}
