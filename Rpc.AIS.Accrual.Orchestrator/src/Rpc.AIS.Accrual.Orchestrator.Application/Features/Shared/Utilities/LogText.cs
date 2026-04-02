using System;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

/// <summary>
/// Utility helpers for keeping log messages within reasonable size limits.
/// </summary>
public static class LogText
{
    /// <summary>
    /// Trims a string to a safe maximum length for logs (App Insights trace size guard).
    /// </summary>
    public static string TrimForLog(string? s, int maxChars = 4000)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        if (maxChars <= 0) return string.Empty;
        return s.Length <= maxChars ? s : string.Concat(s.AsSpan(0, maxChars), " ...");
    }

    /// <summary>
    /// Executes normalize guid string.
    /// </summary>
    public static string NormalizeGuidString(string? maybeGuid)
    {
        if (string.IsNullOrWhiteSpace(maybeGuid)) return string.Empty;
        return maybeGuid.Trim().TrimStart('{').TrimEnd('}');
    }
}
