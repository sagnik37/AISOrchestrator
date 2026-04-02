using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Resilience;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;

public sealed partial class FscmWoPayloadValidationClient
    : Rpc.AIS.Accrual.Orchestrator.Core.Abstractions.IFscmWoPayloadValidationClient
{
    private sealed record ParsedValidationResponse(
     string FilteredPayloadJson,
     WoPayloadValidationFailure[] Failures);

    private static ParsedValidationResponse ParseValidationResponse(string responseBody, string originalPayloadJson)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        // FSCM contract: { "WO Headers": [ { "Status": "...", "Errors": [..], ... } ] }
        if (!TryGetPropertyCaseInsensitive(root, "WO Headers", out var woHeaders) ||
            woHeaders.ValueKind != JsonValueKind.Array)
        {
            // If FSCM returned something unexpected, fail closed.
            throw new InvalidOperationException("Unexpected FSCM validation response: missing 'WO Headers' array.");
        }

        var failures = new List<WoPayloadValidationFailure>(capacity: 16);

        foreach (var header in woHeaders.EnumerateArray())
        {
            var woNumber = GetStringCaseInsensitive(header, "WO Number");
            var woGuidStr = GetStringCaseInsensitive(header, "Work order GUID");
            _ = Guid.TryParse(woGuidStr, out var woGuid);

            var status = GetStringCaseInsensitive(header, "Status") ?? string.Empty;

            if (status.Equals("Failed", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Error", StringComparison.OrdinalIgnoreCase))
            {
                // Prefer "Errors" array if present; otherwise fall back to "Info Message"
                if (TryGetPropertyCaseInsensitive(header, "Errors", out var errorsNode) &&
                    errorsNode.ValueKind == JsonValueKind.Array)
                {
                    foreach (var err in errorsNode.EnumerateArray())
                    {
                        var msg = err.ValueKind == JsonValueKind.String ? err.GetString() : err.ToString();
                        if (string.IsNullOrWhiteSpace(msg)) continue;

                        failures.Add(new WoPayloadValidationFailure(
                            woGuid,
                            woNumber,
                            JournalType.Item,               // caller will override if needed; see note below
                            Guid.Empty,
                            "AIS_FSCM_REMOTE_VALIDATION_FAILED",
                            msg!,
                            ValidationDisposition.Invalid));
                    }
                }
                else
                {
                    var info = GetStringCaseInsensitive(header, "Info Message");
                    if (!string.IsNullOrWhiteSpace(info))
                    {
                        failures.Add(new WoPayloadValidationFailure(
                            woGuid,
                            woNumber,
                            JournalType.Item,
                            Guid.Empty,
                            "AIS_FSCM_REMOTE_VALIDATION_FAILED",
                            info!,
                            ValidationDisposition.Invalid));
                    }
                }
            }
        }

        // FSCM is not returning a filtered payload; keep original.
        return new ParsedValidationResponse(
            FilteredPayloadJson: originalPayloadJson,
            Failures: failures.ToArray());
    }

    private static (string WorkOrderGuid, string? WorkOrderId) TryGetFirstWorkOrderIdentity(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("_request", out var req) || req.ValueKind != JsonValueKind.Object)
                return ("MULTI", null);
            if (!req.TryGetProperty("WOList", out var list) || list.ValueKind != JsonValueKind.Array)
                return ("MULTI", null);
            using var e = list.EnumerateArray();
            if (!e.MoveNext()) return ("MULTI", null);

            var wo = e.Current;
            var guid = GetStringCaseInsensitive(wo, "WorkOrderGUID") ?? GetStringCaseInsensitive(wo, "WorkOrderGuid");
            var id = GetStringCaseInsensitive(wo, "WorkOrderID") ?? GetStringCaseInsensitive(wo, "WorkOrderId") ?? GetStringCaseInsensitive(wo, "WO Number");
            if (string.IsNullOrWhiteSpace(guid)) return ("MULTI", id);
            return (guid.Trim(), id);
        }
        catch
        {
            return ("MULTI", null);
        }
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (p.NameEquals(name) || p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string? GetStringCaseInsensitive(JsonElement obj, string name)
    {
        return TryGetPropertyCaseInsensitive(obj, name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : (TryGetPropertyCaseInsensitive(obj, name, out var v2) ? v2.ToString() : null);
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var p in root.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? TryGetString(JsonElement root, string name)
    {
        return TryGetProperty(root, name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
    }
}
