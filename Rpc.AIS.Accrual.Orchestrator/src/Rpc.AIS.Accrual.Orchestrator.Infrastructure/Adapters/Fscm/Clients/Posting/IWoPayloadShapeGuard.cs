using System;
using System.Text.Json;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;

/// <summary>
/// Validates the WO payload has the minimum required schema/envelope to proceed.
/// This is a "hard fail" guard (schema issues are not line-filterable).
/// </summary>
public interface IWoPayloadShapeGuard
{
    void EnsureValidShapeOrThrow(string normalizedWoPayloadJson);
}

public sealed class WoPayloadShapeGuard : IWoPayloadShapeGuard
{
    public void EnsureValidShapeOrThrow(string normalizedWoPayloadJson)
    {
        if (string.IsNullOrWhiteSpace(normalizedWoPayloadJson))
            throw new ArgumentException("WO payload is empty.");

        using var doc = JsonDocument.Parse(normalizedWoPayloadJson);

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Payload root must be a JSON object.");

        if (!doc.RootElement.TryGetProperty("_request", out var req) || req.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Payload must contain _request object.");

        if (!req.TryGetProperty("WOList", out var woList) || woList.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Payload must contain _request.WOList array.");

        foreach (var wo in woList.EnumerateArray())
        {
            if (wo.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Each element in _request.WOList must be an object.");

            RequireNonEmptyString(wo, "Company");
            RequireNonEmptyString(wo, "SubProjectId");
            RequireNonEmptyString(wo, "WorkOrderGUID");
            RequireNonEmptyString(wo, "WorkOrderID");

            ValidateSectionIfPresent(wo, "WOExpLines");
            ValidateSectionIfPresent(wo, "WOHourLines");
            ValidateSectionIfPresent(wo, "WOItemLines");
        }
    }

    private static void ValidateSectionIfPresent(JsonElement wo, string sectionKey)
    {
        if (!wo.TryGetProperty(sectionKey, out var section))
            return;

        if (section.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"{sectionKey} must be an object.");

        if (!section.TryGetProperty("JournalLines", out var lines) || lines.ValueKind != JsonValueKind.Array)
            throw new ArgumentException($"{sectionKey}.JournalLines must be an array.");
    }

    private static void RequireNonEmptyString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var p) ||
            p.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(p.GetString()))
        {
            throw new ArgumentException($"Missing/empty required field: {name}.");
        }
    }
}
