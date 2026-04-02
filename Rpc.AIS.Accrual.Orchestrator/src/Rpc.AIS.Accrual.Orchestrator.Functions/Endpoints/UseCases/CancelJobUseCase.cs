using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;
using Rpc.AIS.Accrual.Orchestrator.Functions.Services;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Cancel Job use case.
/// Fully extracted (no shared endpoint handler dependency).
/// </summary>
public sealed class CancelJobUseCase : JobOperationsUseCaseBase, ICancelJobUseCase
{
    private readonly IFsaDeltaPayloadOrchestrator _payloadOrch;
    private readonly FsOptions _fsOpt;
    private readonly IPostingClient _posting;
    private readonly IWoDeltaPayloadServiceV2 _deltaV2;
    private readonly IFscmProjectStatusClient _projectStatus;
    private readonly InvoiceAttributeSyncRunner _invoiceSync;
    private readonly InvoiceAttributesUpdateRunner _invoiceUpdate;
    private readonly DocumentAttachmentCopyRunner _docCopy;

    public CancelJobUseCase(
        ILogger<CancelJobUseCase> log,
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

        using var scope = _log.BeginScope(new Dictionary<string, object?>
        {
            ["RunId"] = runId,
            ["CorrelationId"] = correlationId,
            ["SourceSystem"] = sourceSystem,
            ["Function"] = "CancelJob"
        });

        var body = await ReadBodyAsync(req);

        await LogInboundPayloadAsync(runId, correlationId, "CancelJob", body).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(body))
            return await BadRequestAsync(req, correlationId, runId, "Request body is required and must contain workOrderGuid.");

        if (!TryParseFsJobOpsRequest(body, out var parsed, out var parseError))
            return await BadRequestAsync(req, correlationId, runId, parseError ?? "Invalid request body.");

        // Prefer envelope-provided RunId/CorrelationId when present.
        runId = string.IsNullOrWhiteSpace(parsed.RunId) ? runId : parsed.RunId!;
        correlationId = string.IsNullOrWhiteSpace(parsed.CorrelationId) ? correlationId : parsed.CorrelationId!;
        var woGuid = parsed.WorkOrderGuid;

        _log.LogInformation("CANCELJOB_REQUEST_ACCEPTED RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} InitiatedBy={InitiatedBy} Stage={Stage} WorkOrderGuid={WorkOrderGuid}",
            runId, correlationId, sourceSystem, "CancelJob", "FS", "Accepted", woGuid);

        using var woScope = LogScopes.BeginFunctionScope(_log, new LogScopeContext
        {
            Function = "CancelJob",
            Operation = "CancelJob",
            FlowName = "CancelJob",
            Trigger = "Http",
            TriggerName = "CancelJob",
            TriggerChannel = "Http",
            InitiatedBy = "FS",
            RunId = runId,
            CorrelationId = correlationId,
            SourceSystem = sourceSystem,
            WorkOrderGuid = woGuid,
            Stage = "Accepted"
        });

        // Build the FULL WO payload (single WO) even if not OPEN.
        _log.LogInformation("CANCELJOB_STAGE_BEGIN RunId={RunId} CorrelationId={CorrelationId} Stage={Stage} WorkOrderGuid={WorkOrderGuid}",
            runId, correlationId, "FetchFromFsa", woGuid);

        var fsaPayload = await _payloadOrch.BuildSingleWorkOrderAnyStatusAsync(
            new GetFsaDeltaPayloadInputDto(runId, correlationId, "CancelJob", woGuid.ToString()),
            _fsOpt,
            ctx.CancellationToken);

        //  NEW: handle empty/ignored payload → update status as Cancelled anyway.
        var payloadSummary = SummarizeWoPayload(fsaPayload?.PayloadJson);

        _log.LogInformation("CANCELJOB_STAGE_END RunId={RunId} CorrelationId={CorrelationId} Stage={Stage} WorkOrderGuid={WorkOrderGuid} PayloadBytes={PayloadBytes} WorkOrders={WorkOrders} WorkOrderIds={WorkOrderIds} Companies={Companies} SubProjectIds={SubProjectIds}",
            runId, correlationId, "FetchFromFsa", woGuid, payloadSummary.Bytes, payloadSummary.WorkOrders, payloadSummary.WorkOrderIdsCsv, payloadSummary.CompaniesCsv, payloadSummary.SubProjectIdsCsv);

        var payloadEmpty =
            fsaPayload is null ||
            string.IsNullOrWhiteSpace(fsaPayload.PayloadJson) ||
            fsaPayload.WorkOrderNumbers.Count == 0;

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
                    "FSA payload was empty/ignored (no lines). To cancel status anyway, provide Company and SubProjectId in the request envelope (_request.WOList[0]).");
            }

            _log.LogWarning("CANCELJOB_EMPTY_FSA_PAYLOAD RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid} Company={Company} SubProjectId={SubProjectId}",
                runId, correlationId, woGuid, companyFallback, subProjectFallback);

            var runCtx = new RunContext(runId, DateTimeOffset.UtcNow, "CancelJob", correlationId, sourceSystem, companyFallback);

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
                _log.LogError(ex, "Invoice attribute update FAILED (payloadEmpty CancelJob). RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid}", runId, correlationId, woGuid);
                invoiceAttributesUpdate = new { attempted = true, success = false, note = "Invoice attribute update failed (see logs)." };
            }

            await _docCopy.CopyAsync(runCtx, woGuid, companyFallback!, subProjectFallback!, ctx.CancellationToken);

            var woIdForStatus = woGuid.ToString("D"); // best available when FSA WO numbers are missing

            var statusCancelUpdate = await _projectStatus.UpdateAsync(
                runCtx,
                companyFallback!,
                subProjectFallback!,
                woGuid,
                woIdForStatus,
                status: (int)FscmProjectStatus.Cancelled,
                ctx.CancellationToken);

    _log.LogInformation("CANCELJOB_COMPLETED RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid} Mode={Mode} Success={Success}",
            runId, correlationId, woGuid, "EmptyPayloadOrNoDelta", true);

        return await OkAsync(req, correlationId, runId, new
            {
                runId,
                correlationId,
                sourceSystem,
                operation = "CancelJob",
                workOrderGuid = woGuid,
                workOrderNumbers = Array.Empty<string>(),
                subProjectId = subProjectFallback,
                message = "FSA payload was empty/ignored (no lines). Skipped delta/post, but InvoiceAttributesUpdate + DocumentUpload + ProjectStatusUpdate were executed (Cancelled).",
                invoiceAttributesUpdate,
                projectStatusUpdate = new
                {
                    success = statusCancelUpdate.IsSuccess,
                    httpStatus = statusCancelUpdate.HttpStatus
                }
            });
        }

        // Determine Company + SubProjectId.
        // Prefer FS envelope values when provided; otherwise fall back to the FSA payload.
        var company = parsed.Company;
        var subProjectId = parsed.SubProjectId;

        if (string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(subProjectId))
        {
            TryExtractCompanyAndSubProjectIdString(fsaPayload!.PayloadJson, _log, runId, correlationId, out var c2, out var sp2);
            company ??= c2;
            subProjectId ??= sp2;
        }

        if (string.IsNullOrWhiteSpace(company))
            return await BadRequestAsync(req, correlationId, runId, "Company is missing (provide Company in request envelope or ensure FSA payload contains Company).");

        if (string.IsNullOrWhiteSpace(subProjectId))
            return await BadRequestAsync(req, correlationId, runId, "SubProjectId is missing (provide SubProjectId in request envelope or ensure FSA payload contains SubProjectId).");

        var runCtx2 = new RunContext(runId, DateTimeOffset.UtcNow, "CancelJob", correlationId, sourceSystem, company);

        // Build delta payload (compare FSA snapshot vs FSCM history) then validate/post.
        _log.LogInformation("CANCELJOB_STAGE_BEGIN RunId={RunId} CorrelationId={CorrelationId} Stage={Stage} WorkOrderGuid={WorkOrderGuid} Company={Company} SubProjectId={SubProjectId}",
            runId, correlationId, "BuildDelta", woGuid, company, subProjectId);

        var deltaResult = await _deltaV2.BuildDeltaPayloadAsync(
            runCtx2,
            fsaPayload.PayloadJson,
            DateTime.UtcNow.Date,
            new WoDeltaBuildOptions(BaselineSubProjectId: subProjectId, TargetMode: WoDeltaTargetMode.Normal),
            ctx.CancellationToken);

        _log.LogInformation("CANCELJOB_STAGE_END RunId={RunId} CorrelationId={CorrelationId} Stage={Stage} WorkOrderGuid={WorkOrderGuid} WorkOrdersInInput={WorkOrdersInInput} WorkOrdersInOutput={WorkOrdersInOutput} TotalDeltaLines={TotalDeltaLines} ReverseLines={ReverseLines} RecreateLines={RecreateLines}",
            runId, correlationId, "BuildDelta", woGuid, deltaResult.WorkOrdersInInput, deltaResult.WorkOrdersInOutput, deltaResult.TotalDeltaLines, deltaResult.TotalReverseLines, deltaResult.TotalRecreateLines);

        object? invoiceAttributesUpdate2 = null;

        if (string.IsNullOrWhiteSpace(deltaResult.DeltaPayloadJson))
        {
            try
            {
                var enrich = await _invoiceSync.EnrichPostingPayloadAsync(runCtx2, fsaPayload.PayloadJson, ctx.CancellationToken);
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
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Invoice attribute update FAILED (empty delta CancelJob). RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid}", runId, correlationId, woGuid);
                invoiceAttributesUpdate2 = new { attempted = true, success = false, note = "Invoice attribute update failed (see logs)." };
            }

            await _docCopy.CopyAsync(runCtx2, woGuid, company!, subProjectId!, ctx.CancellationToken);

            var woIdForStatusCancel = fsaPayload.WorkOrderNumbers.FirstOrDefault() ?? "UNKNOWN";
            var statusCancelUpdate = await _projectStatus.UpdateAsync(runCtx2, company!, subProjectId!, woGuid, woIdForStatusCancel, status: (int)FscmProjectStatus.Cancelled, ctx.CancellationToken);

            return await OkAsync(req, correlationId, runId, new
            {
                runId,
                correlationId,
                sourceSystem,
                workOrderGuid = woGuid,
                subProjectId = subProjectId,
                message = "Delta payload is empty; nothing to post, but InvoiceAttributesUpdate + DocumentUpload + ProjectStatusUpdate were executed (Cancelled).",
                delta = new
                {
                    workOrdersInInput = deltaResult.WorkOrdersInInput,
                    workOrdersInOutput = deltaResult.WorkOrdersInOutput,
                    totalDeltaLines = deltaResult.TotalDeltaLines,
                    totalReverseLines = deltaResult.TotalReverseLines,
                    totalRecreateLines = deltaResult.TotalRecreateLines
                },
                invoiceAttributesUpdate = invoiceAttributesUpdate2,
                projectStatusUpdate = new
                {
                    success = statusCancelUpdate.IsSuccess,
                    httpStatus = statusCancelUpdate.HttpStatus
                }
            });
        }

        // Stamp JournalDescription + JournalLineDescription for cancellation
        var jobIdForDesc = fsaPayload.WorkOrderNumbers.FirstOrDefault()
                           ?? woGuid.ToString("D");

        var cancelDeltaPayloadJson = StampJournalDescriptions(
            _log,
            runId,
            correlationId,
            deltaResult.DeltaPayloadJson,
            jobIdForDesc,
            subProjectId!,
            action: "Cancel");

        _log.LogInformation("CANCELJOB_STAGE_BEGIN RunId={RunId} CorrelationId={CorrelationId} Stage={Stage} WorkOrderGuid={WorkOrderGuid} DeltaPayloadBytes={DeltaPayloadBytes}",
            runId, correlationId, "ValidateAndPost", woGuid, cancelDeltaPayloadJson?.Length ?? 0);

        var postResults = await _posting.ValidateOnceAndPostAllJournalTypesAsync(
            runCtx2,
            cancelDeltaPayloadJson,
            ctx.CancellationToken);

        var postSummary = SummarizePostResults(postResults);

        _log.LogInformation("CANCELJOB_STAGE_END RunId={RunId} CorrelationId={CorrelationId} Stage={Stage} WorkOrderGuid={WorkOrderGuid} ResultGroups={ResultGroups} SuccessGroups={SuccessGroups} FailureGroups={FailureGroups} WorkOrdersBefore={WorkOrdersBefore} WorkOrdersPosted={WorkOrdersPosted} WorkOrdersFiltered={WorkOrdersFiltered} RetryableWorkOrders={RetryableWorkOrders} RetryableLines={RetryableLines} ErrorCount={ErrorCount} JournalTypes={JournalTypes} FailedJournalTypes={FailedJournalTypes}",
            runId, correlationId, "ValidateAndPost", woGuid, postSummary.ResultGroups, postSummary.SuccessGroups, postSummary.FailureGroups, postSummary.WorkOrdersBefore, postSummary.WorkOrdersPosted, postSummary.WorkOrdersFiltered, postSummary.RetryableWorkOrders, postSummary.RetryableLines, postSummary.ErrorCount, postSummary.JournalTypesCsv, postSummary.FailedJournalTypesCsv);

        var allOk = postResults.Count == 0 || postResults.All(r => r.IsSuccess);

        if (allOk)
        {
            var enrich = await _invoiceSync.EnrichPostingPayloadAsync(runCtx2, fsaPayload.PayloadJson, ctx.CancellationToken);
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

            await _docCopy.CopyAsync(runCtx2, woGuid, company!, subProjectId!, ctx.CancellationToken);
        }

        // Update FSCM project stage/status to Cancelled.
        FscmProjectStatusUpdateResult? statusCancelUpdate2 = null;
        if (allOk)
        {
            var woIdForStatusCancel2 = fsaPayload.WorkOrderNumbers.FirstOrDefault() ?? "UNKNOWN";
            statusCancelUpdate2 = await _projectStatus.UpdateAsync(runCtx2, company!, subProjectId!, woGuid, woIdForStatusCancel2, status: (int)FscmProjectStatus.Cancelled, ctx.CancellationToken);
        }

        _log.LogInformation("CANCELJOB_COMPLETED RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid} Mode={Mode} Success={Success} ResultGroups={ResultGroups} FailureGroups={FailureGroups}",
            runId, correlationId, woGuid, "PostedOrCancelled", allOk, postSummary.ResultGroups, postSummary.FailureGroups);

        return await OkAsync(req, correlationId, runId, new
        {
            runId,
            correlationId,
            sourceSystem,
            operation = "CancelJob",
            workOrderGuid = woGuid,
            workOrderNumbers = fsaPayload.WorkOrderNumbers,
            subProjectId = subProjectId,
            delta = new
            {
                workOrdersInInput = deltaResult.WorkOrdersInInput,
                workOrdersInOutput = deltaResult.WorkOrdersInOutput,
                totalDeltaLines = deltaResult.TotalDeltaLines,
                totalReverseLines = deltaResult.TotalReverseLines,
                totalRecreateLines = deltaResult.TotalRecreateLines
            },
            postResults = postResults.Select(r => new
            {
                journalType = r.JournalType.ToString(),
                success = r.IsSuccess,
                posted = r.WorkOrdersPosted,
                errors = r.Errors?.Count ?? 0
            }),
            invoiceAttributesUpdate = invoiceAttributesUpdate2,
            projectStatusUpdate = statusCancelUpdate2 is null ? null : new
            {
                success = statusCancelUpdate2.IsSuccess,
                httpStatus = statusCancelUpdate2.HttpStatus
            }
        });

    }



    private static string StampJournalDescriptions(
            ILogger log,
            string runId,
            string correlationId,
            string payloadJson,
            string jobId,
            string subProjectId,
            string action)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return payloadJson;

        JsonNode? node;
        try { node = JsonNode.Parse(payloadJson); }
        catch (Exception ex)
        {
            log.LogWarning(ex, "CancelJob: Failed to parse JSON for stamping JournalDescription. RunId={RunId} CorrelationId={CorrelationId}", runId, correlationId);
            return payloadJson;
        }

        if (node is not JsonObject root)
            return payloadJson;

        if (!TryGetWoList(root, out var woList))
            return payloadJson;

        foreach (var woNode in woList.OfType<JsonObject>())
        {
            var woJobId = GetStringLoose(woNode, "WorkOrderID") ?? jobId;
            var woSubProjectId = GetStringLoose(woNode, "SubProjectId") ?? subProjectId;

            var desc = $"{woJobId} - {woSubProjectId} - {action}";

            StampJournal(woNode, AisConstants.WoPayloadSectionKeys.ItemLines, desc);
            StampJournal(woNode, AisConstants.WoPayloadSectionKeys.ExpenseLines, desc);
            StampJournal(woNode, AisConstants.WoPayloadSectionKeys.HourLines, desc);
        }

        // Keep payload compact (no indentation).
        return root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = null
        });
    }

    private static void StampJournal(JsonObject woNode, string journalKey, string desc)
    {
        if (!woNode.TryGetPropertyValue(journalKey, out var jNode) || jNode is not JsonObject journal)
            return;

        journal["JournalDescription"] = desc;

        if (!journal.TryGetPropertyValue("JournalLines", out var linesNode) || linesNode is not JsonArray lines)
            return;

        foreach (var ln in lines.OfType<JsonObject>())
        {
            ln["JournalDescription"] = desc;
            ln["JournalLineDescription"] = desc;
        }
    }

    private static bool TryGetWoList(JsonObject root, out JsonArray woList)
    {
        woList = new JsonArray();

        // Expect: { "_request": { "WOList": [ ... ] } }
        if (!TryGetNodeLoose(root, "_request", out var reqNode) || reqNode is not JsonObject reqObj)
            return false;

        if (!TryGetNodeLoose(reqObj, "WOList", out var listNode) || listNode is not JsonArray arr)
            return false;

        woList = arr;
        return true;
    }

    private static string? GetStringLoose(JsonObject obj, string key)
    {
        if (!TryGetNodeLoose(obj, key, out var n) || n is null)
            return null;

        var s = n.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static bool TryGetNodeLoose(JsonObject obj, string key, out JsonNode? node)
    {
        node = null;

        if (obj.TryGetPropertyValue(key, out node))
            return true;

        foreach (var kv in obj)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                node = kv.Value;
                return true;
            }
        }

        return false;
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
