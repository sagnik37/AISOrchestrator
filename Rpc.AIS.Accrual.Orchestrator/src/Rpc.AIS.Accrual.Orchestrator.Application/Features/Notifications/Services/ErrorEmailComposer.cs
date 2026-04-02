using System.Net;
using System.Text;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services;

/// <summary>
/// Composes an HTML email body for orchestration failures.
/// Adds a clean "Invalid Work Orders (Validation Failures)" section by parsing the standardized
/// error message produced by PostingHttpClient (Code = FSCM_VALIDATION_FAILED_WO).
/// </summary>
public static class ErrorEmailComposer
{
    /// <summary>
    /// Executes compose html.
    /// </summary>
    public static string ComposeHtml(RunContext context, PostResult result)
    {
        var runId = WebUtility.HtmlEncode(context.RunId);
        var correlationId = WebUtility.HtmlEncode(context.CorrelationId);

        var sb = new StringBuilder(16_384);

        sb.AppendLine("<html><body style='font-family:Segoe UI, Arial, sans-serif; font-size:13px;'>");
        sb.AppendLine("<h2 style='margin:0 0 8px 0;'>AIS Orchestrator - Posting / Validation Failures</h2>");

        sb.AppendLine("<div style='margin:0 0 12px 0;'>");
        sb.Append("<b>RunId:</b> ").Append(runId).Append("<br/>");
        sb.Append("<b>CorrelationId:</b> ").Append(correlationId).Append("<br/>");
        sb.Append("<b>Timestamp (UTC):</b> ").Append(WebUtility.HtmlEncode(DateTime.UtcNow.ToString("u"))).Append("<br/>");
        sb.AppendLine("</div>");

        var errors = result?.Errors ?? Array.Empty<PostError>();
        if (errors.Count == 0)
        {
            sb.AppendLine("<p>No errors were captured in the result object.</p>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        // 1) Clean invalid WO section
        var invalidWoRows = BuildInvalidWoRows(errors);
        if (invalidWoRows.Count > 0)
        {
            sb.AppendLine("<h3 style='margin:16px 0 8px 0;'>Invalid Work Orders (Validation Failures)</h3>");
            sb.AppendLine("<p style='margin:0 0 8px 0;'>These work orders failed FSCM validation and were not posted.</p>");
            sb.AppendLine("<table cellpadding='6' cellspacing='0' border='1' style='border-collapse:collapse; width:100%;'>");
            sb.AppendLine("<tr style='background:#f3f3f3;'>");
            sb.AppendLine("<th align='left'>WO Number</th>");
            sb.AppendLine("<th align='left'>WorkOrder GUID</th>");
            sb.AppendLine("<th align='left'>Info Message</th>");
            sb.AppendLine("<th align='left'>Errors</th>");
            sb.AppendLine("</tr>");

            foreach (var row in invalidWoRows)
            {
                sb.AppendLine("<tr>");
                sb.Append("<td>").Append(WebUtility.HtmlEncode(row.WoNumber)).AppendLine("</td>");
                sb.Append("<td>").Append(WebUtility.HtmlEncode(row.WorkOrderGuid)).AppendLine("</td>");
                sb.Append("<td>").Append(WebUtility.HtmlEncode(row.InfoMessage)).AppendLine("</td>");
                sb.Append("<td>").Append(WebUtility.HtmlEncode(row.Errors)).AppendLine("</td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</table>");
        }

        // 2) Other errors (compact)
        var otherErrors = errors
            .Where(e => !string.Equals(e.Code, "FSCM_VALIDATION_FAILED_WO", StringComparison.OrdinalIgnoreCase))
            // Avoid dumping huge raw bodies into email; keep those for App Insights / storage
            .Where(e => !string.Equals(e.Code, "FSCM_VALIDATION_RAW", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (otherErrors.Count > 0)
        {
            sb.AppendLine("<h3 style='margin:16px 0 8px 0;'>Other Errors</h3>");
            sb.AppendLine("<table cellpadding='6' cellspacing='0' border='1' style='border-collapse:collapse; width:100%;'>");
            sb.AppendLine("<tr style='background:#f3f3f3;'>");
            sb.AppendLine("<th align='left'>Code</th>");
            sb.AppendLine("<th align='left'>Message</th>");
            sb.AppendLine("</tr>");

            foreach (var e in otherErrors)
            {
                sb.AppendLine("<tr>");
                sb.Append("<td>").Append(WebUtility.HtmlEncode(e.Code ?? "")).AppendLine("</td>");
                sb.Append("<td>").Append(WebUtility.HtmlEncode(TrimForEmail(e.Message, 1500))).AppendLine("</td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</table>");
        }

        sb.AppendLine("<p style='margin:16px 0 0 0; color:#555;'>");
        sb.AppendLine("For full payloads and raw validation responses, use Application Insights and filter by CorrelationId / RunId.");
        sb.AppendLine("</p>");

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// Executes build invalid wo rows.
    /// </summary>
    private static List<InvalidWoRow> BuildInvalidWoRows(IReadOnlyList<PostError> errors)
    {
        var rows = new List<InvalidWoRow>();

        foreach (var e in errors)
        {
            if (!string.Equals(e.Code, "FSCM_VALIDATION_FAILED_WO", StringComparison.OrdinalIgnoreCase))
                continue;

            if (PostErrorMessageParser.TryParseInvalidWorkOrder(e, out var parsed))
            {
                rows.Add(new InvalidWoRow(parsed.WoNumber, parsed.WorkOrderGuid, parsed.InfoMessage, parsed.Errors));
                continue;
            }

            // Fallback if format changes: still show something readable
            var msg = (e.Message ?? string.Empty).Trim();
            rows.Add(new InvalidWoRow(
                WoNumber: "(unparsed)",
                WorkOrderGuid: "(unparsed)",
                InfoMessage: "Validation failed (message format did not match parser).",
                Errors: TrimForEmail(msg, 2000)));
        }

        return rows;
    }


    /// <summary>
    /// Executes trim for email.
    /// </summary>
    private static string TrimForEmail(string? s, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        s = s.Trim();
        return s.Length <= maxChars ? s : string.Concat(s.AsSpan(0, maxChars), " ...");

    }

    /// <summary>
    /// Carries invalid wo row data.
    /// </summary>
    private sealed record InvalidWoRow(string WoNumber, string WorkOrderGuid, string InfoMessage, string Errors);
}
