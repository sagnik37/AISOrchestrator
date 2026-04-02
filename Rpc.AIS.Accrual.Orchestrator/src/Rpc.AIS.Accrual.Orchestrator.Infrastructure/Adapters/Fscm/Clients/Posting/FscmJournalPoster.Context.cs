using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;

public sealed partial class FscmJournalPoster
{
    private static PostingContextRequest ExtractPostingContext(string payloadJson, ILogger logger, RunContext ctx)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return new PostingContextRequest(null, null);

            if (!doc.RootElement.TryGetProperty("_request", out var req) || req.ValueKind != JsonValueKind.Object)
                return new PostingContextRequest(null, null);

            if (!req.TryGetProperty("WOList", out var woList) || woList.ValueKind != JsonValueKind.Array)
                return new PostingContextRequest(null, null);

            foreach (var wo in woList.EnumerateArray())
            {
                if (wo.ValueKind != JsonValueKind.Object) continue;

                string? company = null;
                string? subProjectId = null;

                if (wo.TryGetProperty("Company", out var c) && c.ValueKind == JsonValueKind.String)
                    company = (c.GetString() ?? string.Empty).Trim();

                if (wo.TryGetProperty("SubProjectId", out var sp) && sp.ValueKind == JsonValueKind.String)
                    subProjectId = (sp.GetString() ?? string.Empty).Trim();
                else if (wo.TryGetProperty("SubProject", out var sp2) && sp2.ValueKind == JsonValueKind.String)
                    subProjectId = (sp2.GetString() ?? string.Empty).Trim();

                return new PostingContextRequest(
                    string.IsNullOrWhiteSpace(company) ? null : company,
                    string.IsNullOrWhiteSpace(subProjectId) ? null : subProjectId);
            }

            return new PostingContextRequest(null, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Unable to extract posting context (Company/SubProjectId) from payload. RunId={RunId} CorrelationId={CorrelationId}.",
                ctx.RunId,
                ctx.CorrelationId);
            return new PostingContextRequest(null, null);
        }
    }

    private HttpPostOutcome? TryValidateEndpoint(FscmEndpointType endpoint, PostingContextRequest contextReq)
    {
        var companyMissing = string.IsNullOrWhiteSpace(contextReq.Company);
        var subProjectRequired = endpoint == FscmEndpointType.JournalValidate || endpoint == FscmEndpointType.JournalCreate;
        var subProjectMissing = subProjectRequired && string.IsNullOrWhiteSpace(contextReq.SubProjectId);

        if (!companyMissing && !subProjectMissing)
            return null;

        var errors = new List<string>(capacity: 2);
        if (companyMissing) errors.Add($"AIS_{endpoint}_MISSING_COMPANY");
        if (subProjectMissing) errors.Add($"AIS_{endpoint}_MISSING_SUBPROJECTID");

        _logger.LogError(
            "FSCM endpoint pre-validation failed. Endpoint={Endpoint} Errors={Errors}",
            endpoint,
            JsonSerializer.Serialize(errors, JsonOpts));

        var body = JsonSerializer.Serialize(new
        {
            error = "Endpoint request validation failed.",
            endpoint = endpoint.ToString(),
            errors
        }, JsonOpts);

        return new HttpPostOutcome(HttpStatusCode.BadRequest, body, 0, string.Empty);
    }

    private sealed record PostingContextRequest(string? Company, string? SubProjectId);
}
