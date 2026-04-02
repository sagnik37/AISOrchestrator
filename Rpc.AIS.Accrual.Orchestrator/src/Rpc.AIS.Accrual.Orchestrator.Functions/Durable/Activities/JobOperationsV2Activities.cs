using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.InvoiceAttributes;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Additive V2 job operation activities.
/// These activities are invoked only by the V2 job operation orchestrators/endpoints.
/// Existing functionality remains unchanged.
/// </summary>
public sealed class JobOperationsV2Activities
{
    private readonly ILogger<JobOperationsV2Activities> _log;
    private readonly IFscmInvoiceAttributesClient _fscmAttrs;
    private readonly IFscmProjectStatusClient _projectStatus;

    public JobOperationsV2Activities(
        ILogger<JobOperationsV2Activities> log,
        IFscmProjectStatusClient projectStatus)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _projectStatus = projectStatus ?? throw new ArgumentNullException(nameof(projectStatus));
    }


    public sealed record UpdateProjectStatusInputDto(
        string RunId,
        string CorrelationId,
        string? SourceSystem,
        Guid WorkOrderGuid,
        Guid SubprojectGuid,
        string NewStatus,
        string? DurableInstanceId = null);

    [Function(nameof(UpdateProjectStatus))]
    public async Task<bool> UpdateProjectStatus([ActivityTrigger] UpdateProjectStatusInputDto input, FunctionContext ctx)
    {
        using var scope = LogScopes.BeginFunctionScope(_log, new LogScopeContext
        {
            Activity = nameof(UpdateProjectStatus),
            Operation = nameof(UpdateProjectStatus),
            Trigger = "Durable",
            RunId = input.RunId,
            CorrelationId = input.CorrelationId,
            SourceSystem = input.SourceSystem,
            WorkOrderGuid = input.WorkOrderGuid,
            DurableInstanceId = input.DurableInstanceId
        });

        var runCtx = new RunContext(input.RunId, DateTimeOffset.UtcNow, "Durable", input.CorrelationId, input.SourceSystem);

        // This client likely still expects Guid subproject. Leaving unchanged.
        var res = await _projectStatus.UpdateAsync(runCtx, input.SubprojectGuid, input.NewStatus, ctx.CancellationToken).ConfigureAwait(false);
        if (!res.IsSuccess)
        {
            _log.LogWarning("Project status update failed. HttpStatus={HttpStatus} Body={Body}", res.HttpStatus, LogText.TrimForLog(res.Body ?? string.Empty));
            return false;
        }

        _log.LogInformation("Project status updated. NewStatus={NewStatus}", input.NewStatus);
        return true;
    }

    private static Dictionary<string, string?> TryReadFsAttributes(string rawJson)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawJson)) return dict;

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("fsAttributes", out var a) && a.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in a.EnumerateObject())
                    dict[p.Name] = p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.ToString();
            }
        }
        catch (Exception ex)
        {
            // best-effort
            ThrottledTrace.Debug(
                key: "JobOperationsV2Activities.TryReadFsAttributes",
                message: "Failed to parse fsAttributes object (best-effort).",
                ex: ex);
        }

        return dict;
    }

    private static bool TryExtractHeaderContext(string rawJson, out string company, out string subProjectId, out string workOrderId)
    {
        company = string.Empty;
        subProjectId = string.Empty;
        workOrderId = string.Empty;

        if (string.IsNullOrWhiteSpace(rawJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(rawJson);

            if (!doc.RootElement.TryGetProperty("_request", out var req) || req.ValueKind != JsonValueKind.Object)
                return false;

            if (!req.TryGetProperty("WOList", out var list) || list.ValueKind != JsonValueKind.Array)
                return false;

            var first = list.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Object)
                return false;

            company = TryReadString(first, "Company") ?? TryReadString(first, "company") ?? string.Empty;
            subProjectId = TryReadString(first, "SubProjectId") ?? TryReadString(first, "subProjectId") ?? string.Empty;

            workOrderId =
                TryReadString(first, "WorkOrderID") ??
                TryReadString(first, "WorkOrderId") ??
                TryReadString(first, "WONumber") ??
                string.Empty;

            company = company.Trim();
            subProjectId = subProjectId.Trim();
            workOrderId = workOrderId.Trim();

            return !string.IsNullOrWhiteSpace(company)
                && !string.IsNullOrWhiteSpace(subProjectId)
                && !string.IsNullOrWhiteSpace(workOrderId);

            static string? TryReadString(JsonElement obj, string prop)
            {
                if (!obj.TryGetProperty(prop, out var p)) return null;
                if (p.ValueKind == JsonValueKind.String) return p.GetString();
                if (p.ValueKind == JsonValueKind.Null) return null;
                return p.ToString();
            }
        }
        catch (Exception ex)
        {
            ThrottledTrace.Debug(
                key: "JobOperationsV2Activities.TryExtractHeaderContext",
                message: "Failed to parse _request.WOList header context (best-effort).",
                ex: ex);
            return false;
        }
    }
}
