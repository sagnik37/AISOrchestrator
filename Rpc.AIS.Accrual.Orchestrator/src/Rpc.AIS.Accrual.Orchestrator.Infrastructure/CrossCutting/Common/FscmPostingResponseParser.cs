using System;
using System.Collections.Generic;
using System.Text.Json;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Utilities;

/// <summary>
/// Parses the FSCM JournalAsync response into a <see cref="PostResult"/>-like shape.
/// Extracted from PostingHttpClient to keep that class focused on HTTP orchestration (SRP) and to allow reuse in tests.
/// </summary>
public static class FscmPostingResponseParser
{
    public static (bool Ok, string? JournalId, string? Message, List<PostError> ParseErrors) TryParse(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);

            // PostToJournalAsync only calls this after HTTP 2xx.
            // Therefore, missing explicit "success" flag should not automatically mean failure.
            var ok = true;
            string? journalId = null;
            string? message = null;

            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var root = doc.RootElement;

                // 1) If explicit success flags exist, honor them.
                if (TryGetBool(root, "isSuccess", out var b1)) ok = b1;
                else if (TryGetBool(root, "IsSuccess", out var b2)) ok = b2;
                else if (TryGetBool(root, "success", out var b3)) ok = b3;
                else if (TryGetBool(root, "Success", out var b4)) ok = b4;

                // 2) FSCM schema: StatusCode + Error + Response
                // Treat StatusCode == 0 and empty Error as success.
                // Treat non-zero StatusCode OR non-empty Error as failure (unless explicit success==true).
                var hasStatusCode = root.TryGetProperty("StatusCode", out var scEl) && scEl.ValueKind == JsonValueKind.Number;
                var statusCode = hasStatusCode ? scEl.GetInt32() : (int?)null;

                var error = TryGetString(root, "Error");
                var response = TryGetString(root, "Response");

                var successFlagPresent =
                    HasProperty(root, "isSuccess") || HasProperty(root, "IsSuccess") ||
                    HasProperty(root, "success") || HasProperty(root, "Success");

                if (!successFlagPresent)
                {
                    if (statusCode.HasValue)
                        ok = statusCode.Value == 0;

                    if (!string.IsNullOrWhiteSpace(error))
                        ok = false;
                }

                // 3) JournalId may not exist in this schema; keep null.
                // If later FSCM adds it, support common keys:
                journalId =
                    TryGetString(root, "journalId") ??
                    TryGetString(root, "JournalId") ??
                    TryGetString(root, "JournalID");

                // 4) Provide a meaningful message for logs/callers
                if (!string.IsNullOrWhiteSpace(error))
                    message = error;
                else if (!string.IsNullOrWhiteSpace(response))
                    message = response;
                else if (statusCode.HasValue)
                    message = ok ? "Posting completed." : $"Posting returned StatusCode={statusCode.Value}.";
                else
                    message = ok ? "Posting completed." : "Posting completed but response indicates failure.";
            }
            else
            {
                // Non-object response but HTTP 2xx: assume success; keep body as message
                ok = true;
                message = "Posting completed (non-object response body).";
            }

            return (ok, journalId, message, new List<PostError>());
        }
        catch
        {
            // If response isn't valid JSON, still treat HTTP 2xx as success and attach raw response
            return (true, null, "Posting completed (response parsing skipped).", new List<PostError>
            {
                new PostError(
                    Code: "FSCM_POST_RESPONSE_PARSE_SKIPPED",
                    Message: "Posting response could not be parsed; raw response attached.",
                    StagingId: null,
                    JournalId: null,
                    JournalDeleted: false,
                    DeleteMessage: responseBody)
            });
        }
    }

    /// <summary>
    /// Executes try get bool.
    /// </summary>
    private static bool TryGetBool(JsonElement root, string prop, out bool value)
    {
        value = default;
        if (!root.TryGetProperty(prop, out var e)) return false;
        if (e.ValueKind == JsonValueKind.True) { value = true; return true; }
        if (e.ValueKind == JsonValueKind.False) { value = false; return true; }
        if (e.ValueKind == JsonValueKind.String && bool.TryParse(e.GetString(), out var b)) { value = b; return true; }
        return false;
    }

    /// <summary>
    /// Executes try get string.
    /// </summary>
    private static string? TryGetString(JsonElement root, string prop)
    {
        if (!root.TryGetProperty(prop, out var e)) return null;
        return e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => e.GetRawText()
        };
    }

    private static bool HasProperty(JsonElement root, string prop) => root.TryGetProperty(prop, out _);
}
