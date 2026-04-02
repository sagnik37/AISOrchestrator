using System;
using System.Collections.Generic;
using System.Linq;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Options;

/// <summary>
/// Provides notification options behavior.
/// </summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    // Recommended 
    public string ErrorDistributionList { get; init; } = string.Empty;

    // Optional legacy support (only used to bind arrays in some environments)
    public string[] ErrorDistributionListArray { get; init; } = Array.Empty<string>();

    // New: where AIS sends details of invalid delta payload records (AIS-side validation failures)
    public string InvalidPayloadDistributionList { get; init; } = string.Empty;

    public string[] InvalidPayloadDistributionListArray { get; init; } = Array.Empty<string>();

    private static readonly char[] RecipientSeparators = new[] { ';', ',', ' ' };

    /// <summary>
    /// Executes get recipients.
    /// </summary>
    public IReadOnlyList<string> GetRecipients()
    {
        // 1) Prefer string-based setting if present
        if (!string.IsNullOrWhiteSpace(ErrorDistributionList))
        {
            return ErrorDistributionList
                .Split(RecipientSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // 2) Fall back to legacy array if present
        if (ErrorDistributionListArray is { Length: > 0 })
        {
            return ErrorDistributionListArray
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Executes get invalid payload recipients.
    /// </summary>
    public IReadOnlyList<string> GetInvalidPayloadRecipients()
    {
        if (!string.IsNullOrWhiteSpace(InvalidPayloadDistributionList))
        {
            return InvalidPayloadDistributionList
                .Split(RecipientSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (InvalidPayloadDistributionListArray is { Length: > 0 })
        {
            return InvalidPayloadDistributionListArray
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Fall back to the general error DL
        return GetRecipients();
    }
}
