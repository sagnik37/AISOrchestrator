using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services;

public sealed partial class CustomerChangeOrchestrator : ICustomerChangeOrchestrator
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null
    };

    private readonly ILogger<CustomerChangeOrchestrator> _log;
    private readonly SubProjectProvisioningService _subProjectSvc;
    private readonly IFsaDeltaPayloadOrchestrator _fsaPayloadOrch;
    private readonly IPostingClient _posting;
    private readonly IWoDeltaPayloadServiceV2 _deltaV2;
    private readonly IFscmProjectStatusClient _projectStatus;
    private readonly FsOptions _fsOpt;
    private readonly InvoiceAttributeSyncRunner _invoiceSync;
    private readonly InvoiceAttributesUpdateRunner _invoiceUpdate;
    private readonly DocumentAttachmentCopyRunner _documentAttachmentCopy;

    public CustomerChangeOrchestrator(
        ILogger<CustomerChangeOrchestrator> log,
        SubProjectProvisioningService subProjectSvc,
        IFsaDeltaPayloadOrchestrator fsaPayloadOrch,
        IPostingClient posting,
        IWoDeltaPayloadServiceV2 deltaV2,
        IFscmProjectStatusClient projectStatus,
        InvoiceAttributeSyncRunner invoiceSync,
        InvoiceAttributesUpdateRunner invoiceUpdate,
        DocumentAttachmentCopyRunner documentAttachmentCopy,
        IOptions<FsOptions> fsOpt)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _subProjectSvc = subProjectSvc ?? throw new ArgumentNullException(nameof(subProjectSvc));
        _fsaPayloadOrch = fsaPayloadOrch ?? throw new ArgumentNullException(nameof(fsaPayloadOrch));
        _posting = posting ?? throw new ArgumentNullException(nameof(posting));
        _deltaV2 = deltaV2 ?? throw new ArgumentNullException(nameof(deltaV2));
        _projectStatus = projectStatus ?? throw new ArgumentNullException(nameof(projectStatus));
        _invoiceSync = invoiceSync ?? throw new ArgumentNullException(nameof(invoiceSync));
        _invoiceUpdate = invoiceUpdate ?? throw new ArgumentNullException(nameof(invoiceUpdate));
        _documentAttachmentCopy = documentAttachmentCopy ?? throw new ArgumentNullException(nameof(documentAttachmentCopy));
        _fsOpt = fsOpt?.Value ?? new FsOptions();
    }

    public async Task<CustomerChangeResultDto> ExecuteAsync(
        RunContext ctx,
        Guid workOrderGuid,
        string rawRequestJson,
        CancellationToken ct)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));

        var req = ParseAndValidateRequest(workOrderGuid, rawRequestJson);

        var company = ResolveCompanyFromRequest(req);
        var resolvedWorkOrderId = ResolveWorkOrderIdFromRequest(req, workOrderGuid);
        var parentProjectId = ResolveParentProjectIdFromRequest(req);
        var projectName = ResolveProjectNameFromRequest(req);
        var isFsaProject = req.IsFsaProject;          // may be null
        var projectStatus = req.ProjectStatus ?? 3;   // default 3 if not provided

        var (fsaPayloadJson, fsaEmpty) = await FetchFsaFullPayloadAsync(ctx, workOrderGuid, ct).ConfigureAwait(false);

        // If FullFetch is NOT empty, prefer canonical Company + WorkOrderId from FullFetch.
        // (Do not fail if FullFetch is missing these—fallback to request-derived values.)
        if (!fsaEmpty)
        {
            var parsed = CustomerChangePayloadReads.TryReadCompanyAndWorkOrderId(fsaPayloadJson!, workOrderGuid, _log, ctx);

            if (!string.IsNullOrWhiteSpace(parsed.Company))
                company = parsed.Company!;

            if (!string.IsNullOrWhiteSpace(parsed.WorkOrderId))
                resolvedWorkOrderId = parsed.WorkOrderId!;
        }

        if (fsaEmpty)
        {
            return await ExecuteProjectMoveOnlyAsync(
                ctx,
                req,
                workOrderGuid,
                company,
                resolvedWorkOrderId,
                parentProjectId,
                projectName,
                isFsaProject,
                projectStatus,
                ct).ConfigureAwait(false);
        }

        return await ExecuteWithLinesAsync(
            ctx,
            req,
            workOrderGuid,
            company,
            resolvedWorkOrderId,
            parentProjectId,
            projectName,
            isFsaProject,
            projectStatus,
            fsaPayloadJson!,
            ct).ConfigureAwait(false);
    }

    private static CustomerChangeRequest ParseAndValidateRequest(Guid workOrderGuid, string rawRequestJson)
    {
        var req = CustomerChangeRequest.TryParse(rawRequestJson)
                  ?? throw new InvalidOperationException("CustomerChange request payload is missing or invalid.");

        if (req.WorkOrderGuid != Guid.Empty && req.WorkOrderGuid != workOrderGuid)
            throw new InvalidOperationException("WorkOrderGuid mismatch between orchestration input and request body.");

        if (string.IsNullOrWhiteSpace(req.OldSubProjectId))
            throw new InvalidOperationException("OldSubProjectId is required for Customer Change.");

        return req;
    }

    private static string ResolveCompanyFromRequest(CustomerChangeRequest req)
    {
        var company = req.LegalEntity; // parsed from FS payload: Company
        if (string.IsNullOrWhiteSpace(company))
            throw new InvalidOperationException("Company/DataAreaId could not be resolved (missing in request payload).");
        return company!;
    }

    private static string ResolveWorkOrderIdFromRequest(CustomerChangeRequest req, Guid workOrderGuid)
        => req.WorkOrderId ?? workOrderGuid.ToString("D");

    private static string ResolveParentProjectIdFromRequest(CustomerChangeRequest req)
    {
        var parentProjectId = req.ParentProjectId;
        if (string.IsNullOrWhiteSpace(parentProjectId))
            throw new InvalidOperationException("ParentProjectId is required (provide it at root or inside NewSubProjectOverrides; TryParse flattens it).");
        return parentProjectId!;
    }

    private static string ResolveProjectNameFromRequest(CustomerChangeRequest req)
        => string.IsNullOrWhiteSpace(req.ProjectName)
            ? $"WO-SubProject-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}"
            : req.ProjectName!;

    private async Task<(string? PayloadJson, bool IsEmpty)> FetchFsaFullPayloadAsync(RunContext ctx, Guid workOrderGuid, CancellationToken ct)
    {
        var fsa = await _fsaPayloadOrch.BuildSingleWorkOrderAnyStatusAsync(
            new GetFsaDeltaPayloadInputDto(
                ctx.RunId,
                ctx.CorrelationId,
                ctx.TriggeredBy ?? "CustomerChange",
                workOrderGuid.ToString("D")),
            _fsOpt,
            ct).ConfigureAwait(false);

        var payloadJson = fsa?.PayloadJson;
        _log.LogInformation("CUSTOMERCHANGE_FETCH_FSA_RESULT RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid} PayloadBytes={PayloadBytes} WorkOrderCount={WorkOrderCount} IsEmpty={IsEmpty}",
            ctx.RunId, ctx.CorrelationId, workOrderGuid, payloadJson?.Length ?? 0, fsa?.WorkOrderNumbers?.Count ?? 0, string.IsNullOrWhiteSpace(payloadJson));
        return (payloadJson, string.IsNullOrWhiteSpace(payloadJson));
    }

    private async Task<CustomerChangeResultDto> ExecuteProjectMoveOnlyAsync(
        RunContext ctx,
        CustomerChangeRequest req,
        Guid workOrderGuid,
        string company,
        string resolvedWorkOrderId,
        string parentProjectId,
        string projectName,
        int? isFsaProject,
        int projectStatus,
        CancellationToken ct)
    {
        _log.LogWarning(
            "CUSTOMERCHANGE_FLOW_PATH RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid} Path={Path} OldSubProjectId={OldSubProjectId} Company={Company}",
            ctx.RunId, ctx.CorrelationId, workOrderGuid, "ProjectMoveOnly", req.OldSubProjectId, company);

        await CancelOldSubProjectAsync(ctx, req, workOrderGuid, company, resolvedWorkOrderId, ct).ConfigureAwait(false);

        _log.LogInformation("CUSTOMERCHANGE_FLOW_PATH RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid} Path={Path} OldSubProjectId={OldSubProjectId}",
            ctx.RunId, ctx.CorrelationId, workOrderGuid, "WithLines", req.OldSubProjectId);

        var newSubProjectId = await CreateNewSubProjectAsync(
            ctx, req, company, parentProjectId, projectName, isFsaProject, projectStatus, ct).ConfigureAwait(false);

        await BestEffortCopyDocumentAttachmentsAsync(ctx, workOrderGuid, company, newSubProjectId, ct).ConfigureAwait(false);

        // Always sync invoice attributes for NEW subproject even when FullFetch is empty.
        var minimalInvoicePayload = BuildMinimalInvoicePayloadJson(
            ctx,
            workOrderGuid,
            resolvedWorkOrderId,
            company,
            newSubProjectId);

        await BestEffortInvoiceAttributesAsync(ctx, minimalInvoicePayload, ct).ConfigureAwait(false);

        return new CustomerChangeResultDto(newSubProjectId);
    }

    private async Task<CustomerChangeResultDto> ExecuteWithLinesAsync(
        RunContext ctx,
        CustomerChangeRequest req,
        Guid workOrderGuid,
        string company,
        string resolvedWorkOrderId,
        string parentProjectId,
        string projectName,
        int? isFsaProject,
        int projectStatus,
        string fsaPayloadJson,
        CancellationToken ct)
    {
        _log.LogInformation("CUSTOMERCHANGE_FLOW_PATH RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid} Path={Path} OldSubProjectId={OldSubProjectId}",
            ctx.RunId, ctx.CorrelationId, workOrderGuid, "WithLines", req.OldSubProjectId);

        var newSubProjectId = await CreateNewSubProjectAsync(
            ctx, req, company, parentProjectId, projectName, isFsaProject, projectStatus, ct).ConfigureAwait(false);

        await BestEffortCopyDocumentAttachmentsAsync(ctx, workOrderGuid, company, newSubProjectId, ct).ConfigureAwait(false);

        // Recreate into NEW subproject via DeltaV2 Normal (Baseline = New subproject)
        var fsaForNew = CustomerChangePayloadReads.RewriteSubProjectId(fsaPayloadJson, newSubProjectId, _log, ctx);
        var todayUtc = DateTime.UtcNow.Date;

        var deltaNew = await _deltaV2.BuildDeltaPayloadAsync(
            ctx,
            fsaForNew,
            todayUtc,
            new WoDeltaBuildOptions(BaselineSubProjectId: newSubProjectId, TargetMode: WoDeltaTargetMode.Normal),
            ct).ConfigureAwait(false);

        await PostDeltaIfAnyAsync(
            ctx,
            phase: "Post NEW",
            subProjectId: newSubProjectId,
            deltaNew.DeltaPayloadJson,
            deltaNew.TotalDeltaLines,
            ct).ConfigureAwait(false);

        // Always sync invoice attributes for NEW subproject, even when deltaNew has 0 lines.
        var invoicePayloadForNew =
            (!string.IsNullOrWhiteSpace(deltaNew.DeltaPayloadJson) ? deltaNew.DeltaPayloadJson! :
             !string.IsNullOrWhiteSpace(fsaForNew) ? fsaForNew :
             BuildMinimalInvoicePayloadJson(ctx, workOrderGuid, resolvedWorkOrderId, company, newSubProjectId));

        await BestEffortInvoiceAttributesAsync(ctx, invoicePayloadForNew, ct).ConfigureAwait(false);

        // Reverse OLD subproject via DeltaV2 CancelToZero (Baseline = Old subproject)
        var fsaForOld = CustomerChangePayloadReads.RewriteSubProjectId(fsaPayloadJson, req.OldSubProjectId!, _log, ctx);

        var deltaOld = await _deltaV2.BuildDeltaPayloadAsync(
            ctx,
            fsaForOld,
            todayUtc,
            new WoDeltaBuildOptions(BaselineSubProjectId: req.OldSubProjectId, TargetMode: WoDeltaTargetMode.CancelToZero),
            ct).ConfigureAwait(false);

        await PostDeltaIfAnyAsync(
            ctx,
            phase: "Reverse OLD",
            subProjectId: req.OldSubProjectId!,
            deltaOld.DeltaPayloadJson,
            deltaOld.TotalDeltaLines,
            ct).ConfigureAwait(false);

        await CancelOldSubProjectAsync(ctx, req, workOrderGuid, company, resolvedWorkOrderId, ct).ConfigureAwait(false);

        return new CustomerChangeResultDto(newSubProjectId);
    }

    private async Task PostDeltaIfAnyAsync(
        RunContext ctx,
        string phase,
        string subProjectId,
        string? deltaPayloadJson,
        int totalDeltaLines,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(deltaPayloadJson) && totalDeltaLines > 0)
        {
            _log.LogInformation("CustomerChange: {Phase} BEGIN SubProjectId={SubProjectId} DeltaLines={Lines}", phase, subProjectId, totalDeltaLines);

            var postRes = await _posting.ValidateOnceAndPostAllJournalTypesAsync(ctx, deltaPayloadJson!, ct).ConfigureAwait(false);
            if (postRes.Any(r => !r.IsSuccess))
                throw new InvalidOperationException($"{phase} failed (see PostResults).");

            _log.LogInformation("CustomerChange: {Phase} OK SubProjectId={SubProjectId}", phase, subProjectId);
            return;
        }

        _log.LogInformation("CustomerChange: No delta lines for phase; skipping post. Phase={Phase} SubProjectId={SubProjectId}", phase, subProjectId);
    }

    private async Task CancelOldSubProjectAsync(
        RunContext ctx,
        CustomerChangeRequest req,
        Guid workOrderGuid,
        string company,
        string resolvedWorkOrderId,
        CancellationToken ct)
    {
        _log.LogInformation("CustomerChange: ProjectStatusUpdate BEGIN OldSubProjectId={Old} Status=6", req.OldSubProjectId);

        var statusRes = await _projectStatus.UpdateAsync(
            ctx,
            company,
            req.OldSubProjectId!,
            workOrderGuid,
            resolvedWorkOrderId,
            status: (int)FscmProjectStatus.Cancelled,
            ct).ConfigureAwait(false);

        if (!statusRes.IsSuccess)
            throw new InvalidOperationException($"ProjectStatusUpdate failed. Http={statusRes.HttpStatus} Body={statusRes.Body}");

        _log.LogInformation("CustomerChange: ProjectStatusUpdate OK OldSubProjectId={Old}", req.OldSubProjectId);
    }

    private async Task<string> CreateNewSubProjectAsync(
        RunContext ctx,
        CustomerChangeRequest req,
        string company,
        string parentProjectId,
        string projectName,
        int? isFsaProject,
        int projectStatus,
        CancellationToken ct)
    {
        var createReq = new SubProjectCreateRequest(
            DataAreaId: company,
            ParentProjectId: parentProjectId,
            ProjectName: projectName,
            CustomerReference: null,
            InvoiceNotes: null,
            ActualStartDate: null,
            ActualEndDate: null,
            AddressName: null,
            Street: null,
            City: null,
            State: null,
            County: null,
            CountryRegionId: null,
            WellLocale: null,
            WellName: null,
            WellNumber: null,
            ProjectStatus: projectStatus)
        {
            WorkOrderGuid = req.WorkOrderGuid == Guid.Empty ? null : req.WorkOrderGuid.ToString("B").ToUpperInvariant(), // "{GUID}"
            IsFsaProject = isFsaProject,
            ProjectStatus = projectStatus,
            WorkOrderId = projectName
        };

        _log.LogInformation(
            "CustomerChange: CreateSubproject BEGIN OldSubProjectId={Old} Company={Company} ParentProjectId={Parent} ProjectName={Name} IsFSAProject={IsFSAProject} ProjectStatus={ProjectStatus}",
            req.OldSubProjectId, createReq.DataAreaId, createReq.ParentProjectId, createReq.ProjectName, createReq.IsFsaProject, createReq.ProjectStatus);

        var createRes = await _subProjectSvc.ProvisionAsync(ctx, createReq, ct).ConfigureAwait(false);

        if (!createRes.IsSuccess || string.IsNullOrWhiteSpace(createRes.parmSubProjectId))
            throw new InvalidOperationException($"Subproject creation failed. Message={createRes.Message}");

        var newSubProjectId = createRes.parmSubProjectId!;
        _log.LogInformation("CustomerChange: CreateSubproject OK NewSubProjectId={New}", newSubProjectId);

        return newSubProjectId;
    }

    private async Task BestEffortInvoiceAttributesAsync(RunContext ctx, string postingPayloadJson, CancellationToken ct)
    {
        try
        {
            var enriched = await _invoiceSync.EnrichPostingPayloadAsync(ctx, postingPayloadJson, ct).ConfigureAwait(false);
            var update = await _invoiceUpdate.UpdateFromPostingPayloadAsync(ctx, enriched.PostingPayloadJson, ct).ConfigureAwait(false);
            _log.LogInformation("CUSTOMERCHANGE_INVOICEATTRIBUTES_COMPLETED RunId={RunId} CorrelationId={CorrelationId} Attempted={Attempted} Success={Success} WorkOrdersWithInvoiceAttributes={WorkOrdersWithInvoiceAttributes} TotalAttributePairs={TotalAttributePairs} UpdateSuccessCount={UpdateSuccessCount} UpdateFailureCount={UpdateFailureCount}",
                ctx.RunId, ctx.CorrelationId, enriched.Attempted, enriched.Success, enriched.WorkOrdersWithInvoiceAttributes, enriched.TotalAttributePairs, update.SuccessCount, update.FailureCount);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "CustomerChange: InvoiceAttributesUpdate FAILED (best-effort). RunId={RunId} CorrelationId={CorrelationId}",
                ctx.RunId, ctx.CorrelationId);
        }
    }

    private async Task BestEffortCopyDocumentAttachmentsAsync(
        RunContext ctx,
        Guid workOrderGuid,
        string company,
        string newSubProjectId,
        CancellationToken ct)
    {
        try
        {
            await _documentAttachmentCopy.CopyAsync(ctx, workOrderGuid, company, newSubProjectId, ct).ConfigureAwait(false);
            _log.LogInformation("CUSTOMERCHANGE_DOCUMENTCOPY_COMPLETED RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid} Company={Company} NewSubProjectId={NewSubProjectId}",
                ctx.RunId, ctx.CorrelationId, workOrderGuid, company, newSubProjectId);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "CustomerChange: DocumentAttachment copy FAILED (best-effort). RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid} Company={Company} NewSubProjectId={NewSubProjectId}",
                ctx.RunId, ctx.CorrelationId, workOrderGuid, company, newSubProjectId);
        }
    }

    /// <summary>
    /// Minimal payload builder used when we have no delta payload and/or FullFetch is empty.
    /// This payload is shaped like the posting envelope and is sufficient for the invoice sync/update pipeline.
    /// </summary>
    private static string BuildMinimalInvoicePayloadJson(
        RunContext ctx,
        Guid workOrderGuid,
        string workOrderId,
        string company,
        string subProjectId)
    {
        var woGuid = "{" + workOrderGuid.ToString("D").ToUpperInvariant() + "}";

        var root = new JsonObject
        {
            ["_request"] = new JsonObject
            {
                ["System"] = "FieldService",
                ["RunId"] = ctx.RunId,
                ["CorrelationId"] = ctx.CorrelationId,
                ["WOList"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["Company"] = company ?? string.Empty,
                        ["WorkOrderGUID"] = woGuid,
                        ["WorkOrderID"] = workOrderId ?? string.Empty,
                        ["SubProjectId"] = subProjectId ?? string.Empty
                    }
                }
            }
        };

        return root.ToJsonString(JsonOpts);
    }

}
