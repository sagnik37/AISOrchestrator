using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;
using Rpc.AIS.Accrual.Orchestrator.Core.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Calls the FSCM custom validation endpoint. Response schema is customer-specific, so parsing is best-effort:
/// - If HTTP 2xx and no recognizable failure array is found, validation is treated as successful.
/// - If HTTP non-2xx, a FailFast or Retryable failure is returned depending on <see cref="PayloadValidationOptions.FailClosedOnFscmCustomValidationError"/>.
/// </summary>
public sealed class FscmCustomValidationClient : IFscmCustomValidationClient
{
    private readonly HttpClient _http;
    private readonly ILogger<FscmCustomValidationClient> _log;
    private readonly PayloadValidationOptions _policy;
    private readonly FscmCustomValidationOptions _options;

    public FscmCustomValidationClient(
        HttpClient http,
        ILogger<FscmCustomValidationClient> log,
        IOptions<PayloadValidationOptions> policy,
        IOptions<FscmCustomValidationOptions> options)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _policy = policy?.Value ?? new PayloadValidationOptions();
        _options = options?.Value ?? new FscmCustomValidationOptions();
    }

    public async Task<IReadOnlyList<WoPayloadValidationFailure>> ValidateAsync(
        RunContext context,
        JournalType journalType,
        string company,
        string woPayloadJson,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(company))
        {
            return new[]
            {
                new WoPayloadValidationFailure(
                    Guid.Empty,
                    null,
                    journalType,
                    null,
                    "FSCM_REMOTE_COMPANY_MISSING",
                    "Company is missing; cannot call FSCM custom validation endpoint.",
                    ValidationDisposition.FailFast)
            };
        }

        if (string.IsNullOrWhiteSpace(woPayloadJson))
            return Array.Empty<WoPayloadValidationFailure>();

        var path = _options.EndpointPath ?? "/api/services/AIS/Validate";
        if (!path.StartsWith('/'))
            path = "/" + path;

        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Headers.TryAddWithoutValidation("x-company", company); // optional; safe if endpoint ignores
        req.Headers.TryAddWithoutValidation("x-journalType", journalType.ToString());

        req.Content = new StringContent(woPayloadJson, Encoding.UTF8, "application/json");

        try
        {
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                return BuildTransportFailure(context, journalType, company, (int)resp.StatusCode, body);

            // Best-effort parse. If unknown schema, treat as success.
            if (string.IsNullOrWhiteSpace(body))
                return Array.Empty<WoPayloadValidationFailure>();

            return TryParseFailures(body, journalType, context);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "FSCM custom validation call failed. JournalType={JournalType}, Company={Company}", journalType, company);
            return BuildExceptionFailure(context, journalType, company, ex);
        }
    }

    private IReadOnlyList<WoPayloadValidationFailure> BuildTransportFailure(
        RunContext context,
        JournalType journalType,
        string company,
        int statusCode,
        string? responseBody)
    {
        var disposition = _policy.FailClosedOnFscmCustomValidationError
            ? ValidationDisposition.FailFast
            : ValidationDisposition.Retryable;

        var msg = $"FSCM custom validation endpoint returned HTTP {statusCode}.";
        if (!string.IsNullOrWhiteSpace(responseBody))
            msg += " Response snippet: " + Truncate(responseBody, 500);

        return new[]
        {
            new WoPayloadValidationFailure(
                Guid.Empty,
                null,
                journalType,
                null,
                "FSCM_REMOTE_HTTP_ERROR",
                msg,
                disposition)
        };
    }

    private IReadOnlyList<WoPayloadValidationFailure> BuildExceptionFailure(
        RunContext context,
        JournalType journalType,
        string company,
        Exception ex)
    {
        var disposition = _policy.FailClosedOnFscmCustomValidationError
            ? ValidationDisposition.FailFast
            : ValidationDisposition.Retryable;

        return new[]
        {
            new WoPayloadValidationFailure(
                Guid.Empty,
                null,
                journalType,
                null,
                "FSCM_REMOTE_EXCEPTION",
                $"FSCM custom validation call failed: {ex.GetType().Name}: {ex.Message}",
                disposition)
        };
    }

    private IReadOnlyList<WoPayloadValidationFailure> TryParseFailures(string json, JournalType journalType, RunContext context)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Customer-specific shape observed in FSCM validation response:
            // {
            //   "WO Headers": [
            //     {
            //       "WO Number": "J-...",
            //       "Work order GUID": "...",
            //       "Status": "Failed",
            //       "Info Message": "...",
            //       "Errors": ["..."]
            //     }
            //   ]
            // }
            // If Status == Failed, we must surface it in App Insights and return failures.
            if (root.TryGetProperty("WO Headers", out var woHeaders) && woHeaders.ValueKind == JsonValueKind.Array)
            {
                var list = new List<WoPayloadValidationFailure>();

                foreach (var h in woHeaders.EnumerateArray())
                {
                    var status = ReadString(h, "Status");
                    if (!string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var woNumber = ReadString(h, "WO Number");
                    var woGuidRaw = ReadString(h, "Work order GUID") ?? ReadString(h, "Work Order GUID");
                    var info = ReadString(h, "Info Message");

                    Guid woGuid = Guid.Empty;
                    if (!string.IsNullOrWhiteSpace(woGuidRaw) && Guid.TryParse(woGuidRaw, out var parsed))
                        woGuid = parsed;

                    var errors = new List<string>();
                    if (h.TryGetProperty("Errors", out var errs) && errs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var e in errs.EnumerateArray())
                        {
                            if (e.ValueKind == JsonValueKind.String)
                            {
                                var s = e.GetString();
                                if (!string.IsNullOrWhiteSpace(s))
                                    errors.Add(s);
                            }
                        }
                    }

                    var errorSummary = errors.Count > 0
                        ? string.Join(" | ", errors)
                        : (info ?? "Remote validation failed.");

                    // App Insights visibility: log at Error level with identifiers.
                    _log.LogError(
                        "FSCM validation failed. RunId={RunId} CorrelationId={CorrelationId} JournalType={JournalType} WO Number={WoNumber} WorkOrderGuid={WorkOrderGuid}. InfoMessage={InfoMessage}. Errors={Errors}",
                        context.RunId,
                        context.CorrelationId,
                        journalType,
                        woNumber,
                        woGuidRaw,
                        info,
                        Truncate(errorSummary, 2000));

                    list.Add(new WoPayloadValidationFailure(
                        woGuid,
                        woNumber,
                        journalType,
                        null,
                        "FSCM_REMOTE_VALIDATION_FAILED",
                        errorSummary,
                        ValidationDisposition.Invalid));
                }

                if (list.Count > 0)
                    return list;
            }

            // Common shapes supported:
            // 1) { "failures": [ { workOrderGuid, workOrderLineGuid, code, message, disposition } ] }
            // 2) { "errors": [ { ... } ] } or { "validationErrors": [ ... ] }
            // 3) { "isValid": false, "messages": [ ... ] }  -> treated as FailFast without per-line details
            foreach (var key in new[] { "failures", "errors", "validationErrors" })
            {
                if (root.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<WoPayloadValidationFailure>();
                    foreach (var e in arr.EnumerateArray())
                    {
                        // Best-effort extraction
                        var woGuid = ReadGuid(e, "workOrderGuid") ?? ReadGuid(e, "workOrderId") ?? Guid.Empty;
                        var woLineGuid = ReadGuid(e, "workOrderLineGuid") ?? ReadGuid(e, "lineGuid");
                        var code = ReadString(e, "code") ?? ReadString(e, "errorCode") ?? "FSCM_REMOTE_VALIDATION_ERROR";
                        var msg = ReadString(e, "message") ?? ReadString(e, "errorMessage") ?? "Remote validation failed.";
                        var disp = ReadDisposition(e) ?? ValidationDisposition.Invalid;

                        list.Add(new WoPayloadValidationFailure(
                            woGuid,
                            ReadString(e, "workOrderNumber"),
                            journalType,
                            woLineGuid,
                            code,
                            msg,
                            disp));
                    }
                    return list;
                }
            }

            // If the endpoint uses { isValid: false, message: "..." } shape
            if (root.TryGetProperty("isValid", out var isValid) && isValid.ValueKind == JsonValueKind.False)
            {
                var msg = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                    ? m.GetString()
                    : "Remote validation returned isValid=false.";
                return new[]
                {
                    new WoPayloadValidationFailure(Guid.Empty, null, journalType, null, "FSCM_REMOTE_INVALID", msg ?? "Remote validation failed.", ValidationDisposition.Invalid)
                };
            }

            return Array.Empty<WoPayloadValidationFailure>();
        }
        catch (Exception ex)
        {
            // Do NOT treat parse failures as success in a financial system.
            // Failing fast prevents accidental posting when FSCM schema changes or returns non-JSON.
            _log.LogWarning(ex,
                "FSCM custom validation response could not be parsed. RunId={RunId} CorrelationId={CorrelationId}. Returning FailFast validation failure.",
                context.RunId,
                context.CorrelationId);

            return new[]
            {
                new WoPayloadValidationFailure(
                    WorkOrderGuid: Guid.Empty,
                    WorkOrderNumber: null,
                    JournalType: (JournalType)0,
                    WorkOrderLineGuid: null,
                    Code: "FSCM_VALIDATION_PARSE_FAILED",
                    Message: "FSCM returned a response that AIS could not parse for custom validation. Run stopped to avoid posting with unknown validation state.",
                    Disposition: ValidationDisposition.FailFast)
            };
        }
    }

    private static string? ReadString(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static Guid? ReadGuid(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g)) return g;
        return null;
    }

    private static ValidationDisposition? ReadDisposition(JsonElement e)
    {
        var raw = ReadString(e, "disposition") ?? ReadString(e, "severity");
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Accept either enum names or common words.
        if (Enum.TryParse<ValidationDisposition>(raw, ignoreCase: true, out var disp))
            return disp;

        return raw.ToLowerInvariant() switch
        {
            "error" => ValidationDisposition.Invalid,
            "warning" => ValidationDisposition.Invalid,
            "retryable" => ValidationDisposition.Retryable,
            "failfast" => ValidationDisposition.FailFast,
            _ => null
        };
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s.Substring(0, max);
}
