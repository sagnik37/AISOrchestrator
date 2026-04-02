using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Shared helpers for JobOperations HTTP use cases.
/// Keeps endpoint adapters/use cases SOLID while preserving existing behavior.
/// </summary>
public abstract partial class JobOperationsUseCaseBase
{
    protected readonly ILogger _log;
    protected readonly IAisLogger _aisLogger;
    protected readonly IAisDiagnosticsOptions _diag;

    protected JobOperationsUseCaseBase(ILogger log, IAisLogger aisLogger, IAisDiagnosticsOptions diag)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _aisLogger = aisLogger ?? throw new ArgumentNullException(nameof(aisLogger));
        _diag = diag ?? throw new ArgumentNullException(nameof(diag));
    }



    /// <summary>
    /// Extract Company and SubProjectId from an FSCM WO payload JSON, logging failures but never throwing.
    /// This overload matches existing call-sites in Cancel/Post/AdHoc use cases.
    /// </summary>
    protected static void TryExtractCompanyAndSubProjectIdString(
        string woPayloadJson,
        ILogger log,
        string runId,
        string correlationId,
        out string? company,
        out string? subProjectId)
    {
        if (log is null) throw new ArgumentNullException(nameof(log));

        if (!TryExtractCompanyAndSubProjectIdString(woPayloadJson, out company, out subProjectId))
        {
            log.LogWarning(
                "RunId={RunId} CorrelationId={CorrelationId} Unable to extract Company/SubProjectId from WO payload JSON (fallback extract failed).",
                runId,
                correlationId);
        }
    }

    protected static bool TryExtractCompanyAndSubProjectIdString(string woPayloadJson, out string? company, out string? subProjectId)
    {
        company = null;
        subProjectId = null;

        try
        {
            using var doc = JsonDocument.Parse(woPayloadJson);
            if (!doc.RootElement.TryGetProperty("_request", out var req) || req.ValueKind != JsonValueKind.Object)
                return false;
            if (!req.TryGetProperty("WOList", out var woList) || woList.ValueKind != JsonValueKind.Array)
                return false;

            var first = woList.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Object)
                return false;

            if (first.TryGetProperty("Company", out var c))
                company = c.ValueKind == JsonValueKind.String ? c.GetString() : c.ToString();

            if (first.TryGetProperty("SubProjectId", out var sp))
                subProjectId = sp.ValueKind == JsonValueKind.String ? sp.GetString() : sp.ToString();

            return !string.IsNullOrWhiteSpace(company) || !string.IsNullOrWhiteSpace(subProjectId);
        }
        catch (Exception ex)
        {
            ThrottledTrace.Debug(
                key: "JobOperationsUseCaseBase.TryExtractCompanyAndSubProjectIdString",
                message: "Failed to parse Company/SubProjectId from WO payload JSON (best-effort).",
                ex: ex);
            return false;
        }
    }

    protected static (string runId, string correlationId, string sourceSystem) ReadContext(HttpRequestData req)
    {
        static string Get(HttpRequestData r, string name)
            => r.Headers.TryGetValues(name, out var v) ? v.FirstOrDefault() ?? string.Empty : string.Empty;

        // Match existing behavior: allow multiple possible header names, fall back to empty.
        var runId = Get(req, "x-run-id");
        if (string.IsNullOrWhiteSpace(runId)) runId = Get(req, "RunId");

        var correlationId = Get(req, "x-correlation-id");
        if (string.IsNullOrWhiteSpace(correlationId)) correlationId = Get(req, "CorrelationId");

        var sourceSystem = Get(req, "x-source-system");
        if (string.IsNullOrWhiteSpace(sourceSystem)) sourceSystem = Get(req, "SourceSystem");

        return (runId, correlationId, sourceSystem);
    }


    protected static (string runId, string correlationId, string sourceSystem) ResolveAdHocAllContext(HttpRequestData req, string? body)
    {
        var (runId, correlationId, sourceSystem) = ReadContext(req);

        if (string.IsNullOrWhiteSpace(body))
            return (runId, correlationId, sourceSystem);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            JsonElement request = default;
            var hasRequest = root.ValueKind == JsonValueKind.Object
                             && root.TryGetProperty("_request", out request)
                             && request.ValueKind == JsonValueKind.Object;

            if (string.IsNullOrWhiteSpace(runId) && hasRequest && request.TryGetProperty("RunId", out var runEl) && runEl.ValueKind == JsonValueKind.String)
                runId = runEl.GetString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(correlationId) && hasRequest && request.TryGetProperty("CorrelationId", out var corrEl) && corrEl.ValueKind == JsonValueKind.String)
                correlationId = corrEl.GetString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(sourceSystem) && root.TryGetProperty("SourceSystem", out var srcEl) && srcEl.ValueKind == JsonValueKind.String)
                sourceSystem = srcEl.GetString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(sourceSystem) && root.TryGetProperty("BusinessEventId", out var beEl) && beEl.ValueKind == JsonValueKind.String)
                sourceSystem = "FSCM";
        }
        catch
        {
            // Best-effort only. Caller already has header-based fallback values.
        }

        return (runId, correlationId, sourceSystem);
    }

    protected static async Task<string> ReadBodyAsync(HttpRequestData req)
    {
        if (req?.Body is null) return string.Empty;
        using var sr = new StreamReader(req.Body);
        return await sr.ReadToEndAsync().ConfigureAwait(false);
    }

    protected static string? TryGetHeader(HttpRequestData req, string name)
        => req.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    protected sealed record ParsedFsJobOpsRequest(
        string? RunId,
        string? CorrelationId,
        string? Company,
        Guid WorkOrderGuid,
        string? SubProjectId);



    protected static bool TryGetBusinessEventId(string json, out string? businessEventId)
    {
        businessEventId = null;

        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (root.TryGetProperty("BusinessEventId", out var be) && be.ValueKind == JsonValueKind.String)
            {
                businessEventId = be.GetString();
                return !string.IsNullOrWhiteSpace(businessEventId);
            }

            if (root.TryGetProperty("_request", out var req) && req.ValueKind == JsonValueKind.Object &&
                req.TryGetProperty("BusinessEventId", out var nestedBe) && nestedBe.ValueKind == JsonValueKind.String)
            {
                businessEventId = nestedBe.GetString();
                return !string.IsNullOrWhiteSpace(businessEventId);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    protected static bool IsTestBusinessEventPayload(string json, out string? businessEventId)
    {
        businessEventId = null;

        if (!TryGetBusinessEventId(json, out businessEventId))
            return false;

        return businessEventId!.IndexOf("BusinessEventsTestEndpointContract", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    protected static bool TryParseFsJobOpsRequest(
        string json,
        out ParsedFsJobOpsRequest parsed,
        out string? error)
    {
        parsed = new ParsedFsJobOpsRequest(null, null, null, Guid.Empty, null);
        error = null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("_request", out var req) || req.ValueKind != JsonValueKind.Object)
            {
                error = "Request must contain '_request' object.";
                return false;
            }

            string? runId = null;
            if (req.TryGetProperty("RunId", out var runProp) && runProp.ValueKind == JsonValueKind.String)
                runId = runProp.GetString();

            string? correlationId = null;
            if (req.TryGetProperty("CorrelationId", out var corrProp) && corrProp.ValueKind == JsonValueKind.String)
                correlationId = corrProp.GetString();

            string? company = null;
            if (req.TryGetProperty("Company", out var compProp) && compProp.ValueKind == JsonValueKind.String)
                company = compProp.GetString();

            // WorkOrder GUID can be at root or inside WOList[0]
            Guid woGuid = Guid.Empty;

            if (req.TryGetProperty("WorkOrderGuid", out var wog1) && TryReadGuidFromElement(wog1, out woGuid)) { }
            else if (req.TryGetProperty("WorkOrderGUID", out var wog2) && TryReadGuidFromElement(wog2, out woGuid)) { }
            else if (req.TryGetProperty("workOrderGuid", out var wog3) && TryReadGuidFromElement(wog3, out woGuid)) { }
            else if (req.TryGetProperty("WOList", out var woList) && woList.ValueKind == JsonValueKind.Array && woList.GetArrayLength() > 0)
            {
                var first = woList[0];
                if (first.ValueKind == JsonValueKind.Object)
                {
                    if (first.TryGetProperty("WorkOrderGuid", out var w1) && TryReadGuidFromElement(w1, out woGuid)) { }
                    else if (first.TryGetProperty("WorkOrderGUID", out var w2) && TryReadGuidFromElement(w2, out woGuid)) { }
                    else if (first.TryGetProperty("workOrderGuid", out var w3) && TryReadGuidFromElement(w3, out woGuid)) { }
                }
            }

            if (woGuid == Guid.Empty)
            {
                error = "Request body is required and must contain workOrderGuid.";
                return false;
            }

            string? subProjectId = null;
            if (req.TryGetProperty("SubProjectId", out var sp1) && sp1.ValueKind == JsonValueKind.String) subProjectId = sp1.GetString();
            if (string.IsNullOrWhiteSpace(subProjectId) && req.TryGetProperty("SubprojectId", out var sp2) && sp2.ValueKind == JsonValueKind.String) subProjectId = sp2.GetString();

            parsed = new ParsedFsJobOpsRequest(runId, correlationId, company, woGuid, subProjectId);
            return true;
        }
        catch (Exception ex)
        {
            error = "Invalid request body. " + ex.Message;
            return false;
        }
    }

    // Test hook (keeps the real parser logic private/protected to the use case layer).
    // This avoids reflection in tests while keeping production behavior identical.
    //internal static bool TryParseFsJobOpsRequest_ForTests(
    //    string json,
    //    out ParsedFsJobOpsRequest parsed,
    //    out string? error)
    //    => TryParseFsJobOpsRequest(json, out parsed, out error);

    private static bool TryReadGuidFromElement(JsonElement e, out Guid guid)
    {
        guid = Guid.Empty;

        if (e.ValueKind == JsonValueKind.String)
        {
            var s = e.GetString();
            return TryReadGuid(s, out guid);
        }

        return false;
    }

    protected static bool TryReadGuid(string? s, out Guid guid)
    {
        guid = Guid.Empty;
        if (string.IsNullOrWhiteSpace(s)) return false;

        s = s.Trim();
        if (s.StartsWith("{", StringComparison.Ordinal)) s = s.Trim('{', '}');

        return Guid.TryParse(s, out guid);
    }
}
