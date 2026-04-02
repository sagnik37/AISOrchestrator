using System;
using System.Text.RegularExpressions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services;

/// <summary>
/// Parses structured fields out of standardized <see cref="PostError"/> messages.
/// </summary>
internal static partial class PostErrorMessageParser

{
    // Example message format produced by PostingHttpClient for invalid WOs:
    // "WO validation failed. WorkOrderGUID=<guid>, WO Number=<wo>. Info=<text> Errors: err1 | err2 | ..."
    [GeneratedRegex(
    @"WO validation failed\.\s*WorkOrderGUID=(?<guid>[^,]+),\s*WO Number=(?<wo>[^.]+)\.\s*Info=(?<info>.*?)(?:\s+Errors:\s*(?<errs>.*))?$",
    RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex InvalidWoRegex();

    internal static bool TryParseInvalidWorkOrder(PostError error, out (string WoNumber, string WorkOrderGuid, string InfoMessage, string Errors) parsed)
    {
        parsed = default;

        if (error is null) return false;
        if (!string.Equals(error.Code, "FSCM_VALIDATION_FAILED_WO", StringComparison.OrdinalIgnoreCase)) return false;

        var msg = (error.Message ?? string.Empty).Trim();
        if (msg.Length == 0) return false;

        var m = InvalidWoRegex().Match(msg);
        if (!m.Success) return false;

        parsed = (
            WoNumber: m.Groups["wo"].Value.Trim(),
            WorkOrderGuid: m.Groups["guid"].Value.Trim(),
            InfoMessage: m.Groups["info"].Value.Trim(),
            Errors: m.Groups["errs"].Success ? m.Groups["errs"].Value.Trim() : string.Empty);

        return true;
    }
}
