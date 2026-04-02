using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;
using Rpc.AIS.Accrual.Orchestrator.Functions.Services;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Post Job use case.
/// Fully extracted (no shared endpoint handler dependency).
/// </summary>
public sealed class PostJobUseCase : JobOperationsUseCaseBase, IPostJobUseCase
{
    private readonly IFsaDeltaPayloadOrchestrator _payloadOrch;
    private readonly FsOptions _fsOpt;
    private readonly IPostingClient _posting;
    private readonly IWoDeltaPayloadServiceV2 _deltaV2;
    private readonly IFscmProjectStatusClient _projectStatus;
    private readonly InvoiceAttributeSyncRunner _invoiceSync;
    private readonly InvoiceAttributesUpdateRunner _invoiceUpdate;
    private readonly DocumentAttachmentCopyRunner _docCopy;

    public PostJobUseCase(
        ILogger<PostJobUseCase> log,
        IAisLogger aisLogger,
        IAisDiagnosticsOptions diag,
        IFsaDeltaPayloadOrchestrator payloadOrch,
        FsOptions fsOpt,
        IPostingClient posting,
        IWoDeltaPayloadServiceV2 deltaV2,
        IFscmProjectStatusClient projectStatus,
        InvoiceAttributeSyncRunner invoiceSync,
        InvoiceAttributesUpdateRunner invoiceUpdate,
        DocumentAttachmentCopyRunner docCopy)
        : base(log, aisLogger, diag)
    {
        _payloadOrch = payloadOrch ?? throw new ArgumentNullException(nameof(payloadOrch));
        _fsOpt = fsOpt ?? throw new ArgumentNullException(nameof(fsOpt));
        _posting = posting ?? throw new ArgumentNullException(nameof(posting));
        _deltaV2 = deltaV2 ?? throw new ArgumentNullException(nameof(deltaV2));
        _projectStatus = projectStatus ?? throw new ArgumentNullException(nameof(projectStatus));
        _invoiceSync = invoiceSync ?? throw new ArgumentNullException(nameof(invoiceSync));
        _invoiceUpdate = invoiceUpdate ?? throw new ArgumentNullException(nameof(invoiceUpdate));
        _docCopy = docCopy ?? throw new ArgumentNullException(nameof(docCopy));
    }

    public async Task<HttpResponseData> ExecuteAsync(HttpRequestData req, FunctionContext ctx)
    {
        var (runId, correlationId, _) = ReadContext(req);
        var sourceSystem = "FS";

        using var scope = LogScopes.BeginFunctionScope(_log, new LogScopeContext
        {
            Function = "PostJob",
            Operation = "PostJob",
            FlowName = "PostJob",
            Trigger = "Http",
            TriggerName = "PostJob",
            TriggerChannel = "Http",
            InitiatedBy = "FS",
            RunId = runId,
            CorrelationId = correlationId,
            SourceSystem = sourceSystem
        });

        var body = await ReadBodyAsync(req);

        await LogInboundPayloadAsync(runId, correlationId, "PostJob", body).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(body))
            return await BadRequestAsync(req, correlationId, runId, "Request body is required and must contain workOrderGuid.");

        if (!TryParseFsJobOpsRequest(body, out var parsed, out var parseError))
            return await BadRequestAsync(req, correlationId, runId, parseError ?? "Invalid request body.");

        // Prefer envelope-provided RunId/CorrelationId when present.
        runId = string.IsNullOrWhiteSpace(parsed.RunId) ? runId : parsed.RunId!;
        correlationId = string.IsNullOrWhiteSpace(parsed.CorrelationId) ? correlationId : parsed.CorrelationId!;
        var woGuid = parsed.WorkOrderGuid;

        _log.LogInformation("POSTJOB_REQUEST_ACCEPTED RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} InitiatedBy={InitiatedBy} Stage={Stage} WorkOrderGuid={WorkOrderGuid}",
            runId, correlationId, sourceSystem, "PostJob", "FS", "Accepted", woGuid);

        using var woScope = LogScopes.BeginFunctionScope(_log, new LogScopeContext
        {
            Function = "PostJob",
            Operation = "PostJob",
            FlowName = "PostJob",
            Trigger = "Http",
            TriggerName = "PostJob",
            TriggerChannel = "Http",
            InitiatedBy = "FS",
            RunId = runId,
            CorrelationId = correlationId,
            SourceSystem = sourceSystem,
            WorkOrderGuid = woGuid,
            Stage = "Accepted"
        });

        // Treat PostJob as a single-job sync run through the same pipeline (OPEN set).
        _log.LogInformation("POSTJOB_STAGE_BEGIN RunId={RunId} CorrelationId={CorrelationId} Stage={Stage} WorkOrderGuid={WorkOrderGuid}",
            runId, correlationId, "FetchFromFsa", woGuid);

        var payload = await _payloadOrch.BuildSingleWorkOrderAnyStatusAsync(
            new GetFsaDeltaPayloadInputDto(runId, correlationId, "PostJob", woGuid.ToString()),
            _fsOpt,
            ctx.CancellationToken);

        // If payload is empty/ignored (no lines), still do:
        // 1) Invoice Attributes sync+update (NEW REQUIREMENT)
        // 2) ProjectStatusUpdate using request envelope values
        var payloadSummary = SummarizeWoPayload(payload?.PayloadJson);

        _log.LogInformation("POSTJOB_STAGE_END RunId={RunId} CorrelationId={CorrelationId} Stage={Stage} WorkOrderGuid={WorkOrderGuid} PayloadBytes={PayloadBytes} WorkOrders={WorkOrders} WorkOrderIds={WorkOrderIds} Companies={Companies} SubProjectIds={SubProjectIds}",
            runId, correlationId, "FetchFromFsa", woGuid, payloadSummary.Bytes, payloadSummary.WorkOrders, payloadSummary.WorkOrderIdsCsv, payloadSummary.CompaniesCsv, payloadSummary.SubProjectIdsCsv);

        var payloadEmpty =
            payload is null ||
            string.IsNullOrWhiteSpace(payload.PayloadJson) ||
            payload.WorkOrderNumbers.Count == 0;

        if (payloadEmpty)
        {
            var companyFallback = parsed.Company;
            var subProjectFallback = parsed.SubProjectId;

            if (string.IsNullOrWhiteSpace(companyFallback) || string.IsNullOrWhiteSpace(subProjectFallback))
            {
                return await BadRequestAsync(
                    req,
                    correlationId,
                    runId,
                    "FSA FullFetch returned empty/ignored payload. To proceed, provide Company and SubProjectId in the request envelope (_request.WOList[0]).");
            }

            _log.LogWarning("POSTJOB_EMPTY_FSA_PAYLOAD RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid} Company={Company} SubProjectId={SubProjectId}",
                runId, correlationId, woGuid, companyFallback, subProjectFallback);

            var runCtx = new RunContext(runId, DateTimeOffset.UtcNow, "PostJob", correlationId, sourceSystem, companyFallback);

            // Build a minimal posting-envelope JSON so Invoice sync/update can still run.
            var minimalInvoicePayloadJson = BuildMinimalPostingEnvelopeJson(
                runId: runId,
                correlationId: correlationId,
                company: companyFallback!,
                subProjectId: subProjectFallback!,
                workOrderGuid: woGuid,
                workOrderId: null);

            object? invoiceAttributesUpdate = null;
            try
            {
                var enrich = await _invoiceSync.EnrichPostingPayloadAsync(runCtx, minimalInvoicePayloadJson, ctx.CancellationToken);
                var upd = await _invoiceUpdate.UpdateFromPostingPayloadAsync(runCtx, enrich.PostingPayloadJson, ctx.CancellationToken);

                invoiceAttributesUpdate = new
                {
                    attempted = enrich.Attempted,
                    success = enrich.Success,
                    workOrdersWithInvoiceAttributes = enrich.WorkOrdersWithInvoiceAttributes,
                    totalAttributePairs = enrich.TotalAttributePairs,
                    note = enrich.Note,
                    update = new
                    {
                        upd.WorkOrdersConsidered,
                        upd.WorkOrdersWithUpdates,
                        upd.UpdatePairs,
                        upd.SuccessCount,
                        upd.FailureCount
                    }
                };
            }
            catch (Exception ex)
            {
                // Do NOT block status update; just report invoice update failure.
                _log.LogError(ex, "Invoice attribute update FAILED (payloadEmpty PostJob). RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid}", runId, correlationId, woGuid);
                invoiceAttributesUpdate = new { attempted = true, success = false, note = "Invoice attribute update failed (see logs)." };
            }

            // Copy Work Order document attachments (best-effort).
            await _docCopy.CopyAsync(runCtx, woGuid, companyFallback!, subProjectFallback!, ctx.CancellationToken);

            var woIdForStatus = woGuid.ToString("D"); // best available (since payload has no WorkOrderNumbers)
            var statusUpdate = await _projectStatus.UpdateAsync(
                runCtx,
                companyFallback!,
                subProjectFallback!,
                woGuid,
                woIdForStatus,
                status: (int)FscmProjectStatus.Posted,
                ctx.CancellationToken);

    _log.LogInformation("POSTJOB_COMPLETED RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid} Mode={Mode} Success={Success}",
            runId, correlationId, woGuid, "EmptyPayloadOrNoDelta", true);

        return await OkAsync(req, correlationId, runId, new
            {
                runId,
                correlationId,
                sourceSystem,
                operation = "PostJob",
                workOrderGuid = woGuid,
                workOrderNumbers = Array.Empty<string>(),
                message = "FSA FullFetch payload was empty/ignored (no lines). Skipped delta/post, but InvoiceAttributesUpdate + ProjectStatusUpdate were executed.",
                invoiceAttributesUpdate,
                projectStatusUpdate = new
                {
                    success = statusUpdate.IsSuccess,
                    httpStatus = statusUpdate.HttpStatus
                }
            });
        }

        // Build delta payload (V2) then optionally post.
        if (!TryExtractCompanyAndSubProjectIdString(payload!.PayloadJson, out var company, out var subProjectId) ||
            string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(subProjectId))
            return await BadRequestAsync(req, correlationId, runId, "Company/SubProjectId missing in FSA payload (ensure payload contains Company and SubProjectId).");

        var runCtx2 = new RunContext(runId, DateTimeOffset.UtcNow, "PostJob", correlationId, sourceSystem, company);

        _log.LogInformation("POSTJOB_STAGE_BEGIN RunId={RunId} CorrelationId={CorrelationId} Stage={Stage} WorkOrderGuid={WorkOrderGuid} Company={Company} SubProjectId={SubProjectId}",
            runId, correlationId, "BuildDelta", woGuid, company, subProjectId);

        var delta = await _deltaV2.BuildDeltaPayloadAsync(
            runCtx2,
            payload.PayloadJson,
            DateTime.UtcNow.Date,
            new WoDeltaBuildOptions(BaselineSubProjectId: subProjectId!, TargetMode: WoDeltaTargetMode.Normal),
            ctx.CancellationToken);
        var hasNothingToPost = string.IsNullOrWhiteSpace(delta.DeltaPayloadJson) || delta.TotalDeltaLines <= 0;

        List<PostResult> postResults = new();
        if (hasNothingToPost)
        {
            postResults = await _posting.ValidateOnceAndPostAllJournalTypesAsync(
                runCtx2,
                delta.DeltaPayloadJson!,
                ctx.CancellationToken);
        }
        if (!string.IsNullOrWhiteSpace(delta.DeltaPayloadJson) && delta.TotalDeltaLines > 0)
            postResults = await _posting.ValidateOnceAndPostAllJournalTypesAsync(runCtx2, delta.DeltaPayloadJson!, ctx.CancellationToken);

        var postSummary = SummarizePostResults(postResults);

        _log.LogInformation("POSTJOB_STAGE_END RunId={RunId} CorrelationId={CorrelationId} Stage={Stage} WorkOrderGuid={WorkOrderGuid} ResultGroups={ResultGroups} SuccessGroups={SuccessGroups} FailureGroups={FailureGroups} WorkOrdersBefore={WorkOrdersBefore} WorkOrdersPosted={WorkOrdersPosted} WorkOrdersFiltered={WorkOrdersFiltered} RetryableWorkOrders={RetryableWorkOrders} RetryableLines={RetryableLines} ErrorCount={ErrorCount} JournalTypes={JournalTypes} FailedJournalTypes={FailedJournalTypes}",
            runId, correlationId, "ValidateAndPost", woGuid, postSummary.ResultGroups, postSummary.SuccessGroups, postSummary.FailureGroups, postSummary.WorkOrdersBefore, postSummary.WorkOrdersPosted, postSummary.WorkOrdersFiltered, postSummary.RetryableWorkOrders, postSummary.RetryableLines, postSummary.ErrorCount, postSummary.JournalTypesCsv, postSummary.FailedJournalTypesCsv);

        var allOk = postResults.Count == 0 || postResults.All(r => r.IsSuccess);

        // Invoice attributes update (sync logic + update endpoint) - runs even when there are no journal deltas.
        _log.LogInformation("POSTJOB_STAGE_END RunId={RunId} CorrelationId={CorrelationId} Stage={Stage} WorkOrderGuid={WorkOrderGuid} WorkOrdersInInput={WorkOrdersInInput} WorkOrdersInOutput={WorkOrdersInOutput} TotalDeltaLines={TotalDeltaLines} ReverseLines={ReverseLines} RecreateLines={RecreateLines}",
            runId, correlationId, "BuildDelta", woGuid, delta.WorkOrdersInInput, delta.WorkOrdersInOutput, delta.TotalDeltaLines, delta.TotalReverseLines, delta.TotalRecreateLines);

        object? invoiceAttributesUpdate2 = null;
        if (allOk)
        {
            var enrich = await _invoiceSync.EnrichPostingPayloadAsync(runCtx2, payload.PayloadJson, ctx.CancellationToken);
            var upd = await _invoiceUpdate.UpdateFromPostingPayloadAsync(runCtx2, enrich.PostingPayloadJson, ctx.CancellationToken);

            invoiceAttributesUpdate2 = new
            {
                attempted = enrich.Attempted,
                success = enrich.Success,
                workOrdersWithInvoiceAttributes = enrich.WorkOrdersWithInvoiceAttributes,
                totalAttributePairs = enrich.TotalAttributePairs,
                note = enrich.Note,
                update = new
                {
                    upd.WorkOrdersConsidered,
                    upd.WorkOrdersWithUpdates,
                    upd.UpdatePairs,
                    upd.SuccessCount,
                    upd.FailureCount
                }
            };

            // Copy Work Order document attachments (best-effort).
            await _docCopy.CopyAsync(runCtx2, woGuid, company!, subProjectId!, ctx.CancellationToken);
        }

        // Project status update (ONLY for /job/post).
        FscmProjectStatusUpdateResult? statusUpdate2 = null;
        if (allOk)
        {
            var woIdForStatus2 = payload.WorkOrderNumbers.FirstOrDefault() ?? "UNKNOWN";
            statusUpdate2 = await _projectStatus.UpdateAsync(runCtx2, company!, subProjectId!, woGuid, woIdForStatus2, status: (int)FscmProjectStatus.Posted, ctx.CancellationToken);
        }

        _log.LogInformation("POSTJOB_COMPLETED RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid} Mode={Mode} Success={Success} ResultGroups={ResultGroups} FailureGroups={FailureGroups}",
            runId, correlationId, woGuid, "PostedOrCancelled", allOk, postSummary.ResultGroups, postSummary.FailureGroups);

        return await OkAsync(req, correlationId, runId, new
        {
            runId,
            correlationId,
            sourceSystem,
            operation = "PostJob",
            workOrderGuid = woGuid,
            workOrderNumbers = payload.WorkOrderNumbers,
            delta = new
            {
                delta.WorkOrdersInInput,
                delta.WorkOrdersInOutput,
                delta.TotalDeltaLines,
                delta.TotalReverseLines,
                delta.TotalRecreateLines
            },
            postResults = postResults.Select(r => new
            {
                journalType = r.JournalType.ToString(),
                success = r.IsSuccess,
                posted = r.WorkOrdersPosted,
                errors = r.Errors?.Count ?? 0
            }),
            invoiceAttributesUpdate = invoiceAttributesUpdate2,
            projectStatusUpdate = statusUpdate2 is null ? null : new { success = statusUpdate2.IsSuccess, httpStatus = statusUpdate2.HttpStatus }
        });

    }

    private static string BuildMinimalPostingEnvelopeJson(
            string runId,
            string correlationId,
            string company,
            string subProjectId,
            Guid workOrderGuid,
            string? workOrderId)
    {
        var woObj = new JsonObject
        {
            ["Company"] = company,
            ["SubProjectId"] = subProjectId,
            ["WorkOrderGUID"] = "{" + workOrderGuid.ToString("D").ToUpperInvariant() + "}"
        };

        if (!string.IsNullOrWhiteSpace(workOrderId))
            woObj["WorkOrderID"] = workOrderId;

        var root = new JsonObject
        {
            ["_request"] = new JsonObject
            {
                ["RunId"] = runId,
                ["CorrelationId"] = correlationId,
                ["WOList"] = new JsonArray(woObj)
            }
        };

        return root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = null
        });
    }

    
}
