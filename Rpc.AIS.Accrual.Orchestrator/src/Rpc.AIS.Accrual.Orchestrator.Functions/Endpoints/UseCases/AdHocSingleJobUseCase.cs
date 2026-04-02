using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
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
/// AdHoc Batch - Single Job use case.
/// Fully extracted (no shared endpoint handler dependency).
/// </summary>
public sealed class AdHocSingleJobUseCase : JobOperationsUseCaseBase, IAdHocSingleJobUseCase
{
    private readonly IFsaDeltaPayloadOrchestrator _payloadOrch;
    private readonly FsOptions _fsOpt;
    private readonly IPostingClient _posting;
    private readonly IWoDeltaPayloadServiceV2 _deltaV2;
    private readonly InvoiceAttributeSyncRunner _invoiceSync;
    private readonly InvoiceAttributesUpdateRunner _invoiceUpdate;
    private readonly DocumentAttachmentCopyRunner _docCopy;
    private static readonly ConcurrentDictionary<string, string> _adHocSingleInFlight = new(StringComparer.OrdinalIgnoreCase);
    public AdHocSingleJobUseCase(
        ILogger<AdHocSingleJobUseCase> log,
        IAisLogger aisLogger,
        IAisDiagnosticsOptions diag,
        IFsaDeltaPayloadOrchestrator payloadOrch,
        FsOptions fsOpt,
        IPostingClient posting,
        IWoDeltaPayloadServiceV2 deltaV2,
        InvoiceAttributeSyncRunner invoiceSync,
        InvoiceAttributesUpdateRunner invoiceUpdate,
        DocumentAttachmentCopyRunner docCopy)
        : base(log, aisLogger, diag)
    {
        _payloadOrch = payloadOrch ?? throw new ArgumentNullException(nameof(payloadOrch));
        _fsOpt = fsOpt ?? throw new ArgumentNullException(nameof(fsOpt));
        _posting = posting ?? throw new ArgumentNullException(nameof(posting));
        _deltaV2 = deltaV2 ?? throw new ArgumentNullException(nameof(deltaV2));
        _invoiceSync = invoiceSync ?? throw new ArgumentNullException(nameof(invoiceSync));
        _invoiceUpdate = invoiceUpdate ?? throw new ArgumentNullException(nameof(invoiceUpdate));
        _docCopy = docCopy ?? throw new ArgumentNullException(nameof(docCopy));
    }

    public async Task<HttpResponseData> ExecuteAsync(HttpRequestData req, FunctionContext ctx)
    {
        var started = Stopwatch.StartNew();
        var (runId, correlationId, _) = ReadContext(req);
        var sourceSystem = "FSCM";
        var body = await ReadBodyAsync(req);

        string? requestCompany = null;
        string? requestWorkOrderId = null;
        Guid? requestWorkOrderGuid = null;

        if (!string.IsNullOrWhiteSpace(body) && TryParseFsJobOpsRequest(body, out var parsedForContext, out _))
        {
            runId = string.IsNullOrWhiteSpace(parsedForContext.RunId) ? runId : parsedForContext.RunId!;
            correlationId = string.IsNullOrWhiteSpace(parsedForContext.CorrelationId) ? correlationId : parsedForContext.CorrelationId!;
            requestCompany = parsedForContext.Company;
            requestWorkOrderGuid = parsedForContext.WorkOrderGuid == Guid.Empty ? null : parsedForContext.WorkOrderGuid;
            requestWorkOrderId = TryExtractWorkOrderId(body);
        }

        requestCompany ??= TryExtractBusinessEventLegalEntity(body);

        using var scope = LogScopes.BeginFunctionScope(_log, new LogScopeContext
        {
            Function = "AdHocBatch_SingleJob",
            Operation = "AdHocBatch_SingleJob",
            FlowName = "AdHocSingle",
            Trigger = "Http",
            TriggerName = "AdHocSingle",
            TriggerChannel = "Http",
            InitiatedBy = "FSCM",
            RunId = runId,
            CorrelationId = correlationId,
            SourceSystem = sourceSystem,
            Company = requestCompany,
            WorkOrderId = requestWorkOrderId,
            WorkOrderGuid = requestWorkOrderGuid,
            Stage = TelemetryConventions.Stages.Inbound,
            Outcome = TelemetryConventions.Outcomes.Accepted
        });

        _log.LogInformation(
            "ADHOC_SINGLE_REQUEST_ACCEPTED RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} InitiatedBy={InitiatedBy} Stage={Stage} Outcome={Outcome} DataAreaId={DataAreaId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid} BodyBytes={BodyBytes}",
            runId,
            correlationId,
            sourceSystem,
            "AdHocSingle",
            "FSCM",
            TelemetryConventions.Stages.Inbound,
            TelemetryConventions.Outcomes.Accepted,
            requestCompany,
            requestWorkOrderId,
            requestWorkOrderGuid,
            body?.Length ?? 0);

        if (string.IsNullOrWhiteSpace(body))
            return await BadRequestAsync(req, correlationId, runId, "Request body is required and must contain workOrderGuid.");

        if (IsTestBusinessEventPayload(body, out var businessEventId))
        {
            _log.LogInformation(
                "AdHocBatch_SingleJob received test business event payload. Returning 202 immediately. BusinessEventId={BusinessEventId}",
                businessEventId);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var responsePayload = new
            {
                BusinessEventId = root.GetProperty("BusinessEventId").GetString(),
                BusinessEventLegalEntity = root.GetProperty("BusinessEventLegalEntity").GetString(),
                EventId = root.GetProperty("EventId").GetString(),
                EventTimeIso8601 = root.GetProperty("EventTimeIso8601").GetString(),
                Status = "Accepted",
                Message = "Test business event received. Request acknowledged; no action was executed."
            };

            var response = req.CreateResponse(System.Net.HttpStatusCode.Accepted);
            response.Headers.Add("Content-Type", "application/json");
            var json = JsonSerializer.Serialize(responsePayload);
            _log.LogInformation("Response to FSCM: {ResponseJson}", json);
            await response.WriteStringAsync(json);
            return response;
        }

        await LogInboundPayloadAsync(runId, correlationId, "AdHocBatch_SingleJob", body).ConfigureAwait(false);

        if (!TryParseFsJobOpsRequest(body, out var parsed, out var parseError))
            return await BadRequestAsync(req, correlationId, runId, parseError ?? "Invalid request body.");

        runId = string.IsNullOrWhiteSpace(parsed.RunId) ? runId : parsed.RunId!;
        correlationId = string.IsNullOrWhiteSpace(parsed.CorrelationId) ? correlationId : parsed.CorrelationId!;
        var woGuid = parsed.WorkOrderGuid;
        requestWorkOrderGuid = woGuid == Guid.Empty ? requestWorkOrderGuid : woGuid;
        requestCompany ??= parsed.Company;
        requestCompany ??= TryExtractBusinessEventLegalEntity(body);
        requestWorkOrderId ??= TryExtractWorkOrderId(body);

        if (woGuid == Guid.Empty)
            return await BadRequestAsync(req, correlationId, runId, "WorkOrderGuid is required.");

        if (string.IsNullOrWhiteSpace(requestCompany))
            return await BadRequestAsync(req, correlationId, runId, "Company / legal entity is required.");

        var singleFlightKey = $"ADHOCSINGLE|{requestCompany.Trim().ToUpperInvariant()}|{woGuid:D}".ToUpperInvariant();

        if (!_adHocSingleInFlight.TryAdd(singleFlightKey, runId))
        {
            _log.LogWarning(
                "ADHOC_SINGLE_DUPLICATE_SKIPPED RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} DataAreaId={DataAreaId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid} LockKey={LockKey} Reason=InMemoryLockAlreadyHeld",
                runId, correlationId, sourceSystem, "AdHocSingle", requestCompany, requestWorkOrderId, woGuid, singleFlightKey);

            var duplicateResponse = req.CreateResponse(System.Net.HttpStatusCode.Accepted);
            duplicateResponse.Headers.Add("Content-Type", "application/json");

            await duplicateResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                runId,
                correlationId,
                sourceSystem,
                company = requestCompany,
                workOrderGuid = woGuid,
                workOrderId = requestWorkOrderId,
                status = "Skipped",
                message = "Duplicate request skipped because another run is already in progress for the same work order."
            }));

            return duplicateResponse;
        }

        try
        {
            using var woScope = LogScopes.BeginFunctionScope(_log, new LogScopeContext
            {
                Function = "AdHocBatch_SingleJob",
                Operation = "AdHocBatch_SingleJob",
                FlowName = "AdHocSingle",
                Trigger = "Http",
                TriggerName = "AdHocSingle",
                TriggerChannel = "Http",
                InitiatedBy = "FSCM",
                RunId = runId,
                CorrelationId = correlationId,
                SourceSystem = sourceSystem,
                Company = requestCompany,
                WorkOrderId = requestWorkOrderId,
                WorkOrderGuid = woGuid,
                Stage = TelemetryConventions.Stages.Accepted,
                Outcome = TelemetryConventions.Outcomes.Accepted
            });

            _log.LogInformation(
                "ADHOC_SINGLE_LOCK_ACQUIRED RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} DataAreaId={DataAreaId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid} LockKey={LockKey}",
                runId, correlationId, sourceSystem, "AdHocSingle", requestCompany, requestWorkOrderId, woGuid, singleFlightKey);

            _log.LogInformation(
                "ADHOC_SINGLE_STAGE_BEGIN RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} Stage={Stage} Outcome={Outcome} DataAreaId={DataAreaId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid}",
                runId, correlationId, sourceSystem, "AdHocSingle", TelemetryConventions.Stages.FetchFromFsaBegin, TelemetryConventions.Outcomes.Accepted, requestCompany, requestWorkOrderId, woGuid);

            var payload = await _payloadOrch.BuildSingleWorkOrderAnyStatusAsync(
                new GetFsaDeltaPayloadInputDto(runId, correlationId, "AdHocSingle", woGuid.ToString()),
                _fsOpt,
                ctx.CancellationToken);

            _log.LogInformation(
                "ADHOC_SINGLE_STAGE_END RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} Stage={Stage} Outcome={Outcome} DataAreaId={DataAreaId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid} WorkOrderCount={WorkOrderCount}",
                runId, correlationId, sourceSystem, "AdHocSingle", TelemetryConventions.Stages.FetchFromFsaEnd,
                payload is null || string.IsNullOrWhiteSpace(payload.PayloadJson) || payload.WorkOrderNumbers.Count == 0 ? TelemetryConventions.Outcomes.Skipped : TelemetryConventions.Outcomes.Success,
                requestCompany, requestWorkOrderId, woGuid, payload?.WorkOrderNumbers?.Count ?? 0);

            if (payload is null || string.IsNullOrWhiteSpace(payload.PayloadJson) || payload.WorkOrderNumbers.Count == 0)
                return await NotFoundAsync(req, correlationId, runId, new
                {
                    runId,
                    correlationId,
                    sourceSystem,
                    workOrderGuid = woGuid,
                    message = "Work order not found in OPEN set (or was skipped due to missing SubProject)."
                });

            if (!TryExtractCompanyAndSubProjectIdString(payload.PayloadJson, out var company, out var subProjectId) ||
                string.IsNullOrWhiteSpace(subProjectId))
                return await BadRequestAsync(req, correlationId, runId, "SubProjectId is missing (ensure FSA payload contains SubProjectId).");

            requestCompany = company;
            requestWorkOrderId ??= payload.WorkOrderNumbers.FirstOrDefault();
            var runCtx = new RunContext(runId, DateTimeOffset.UtcNow, "AdHocSingle", correlationId, sourceSystem, company);

            _log.LogInformation(
                "ADHOC_SINGLE_STAGE_BEGIN RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} Stage={Stage} Outcome={Outcome} DataAreaId={DataAreaId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid}",
                runId, correlationId, sourceSystem, "AdHocSingle", TelemetryConventions.Stages.DeltaBuildBegin, TelemetryConventions.Outcomes.Accepted, company, requestWorkOrderId, woGuid);

            var delta = await _deltaV2.BuildDeltaPayloadAsync(
                runCtx,
                payload.PayloadJson,
                DateTime.UtcNow.Date,
                new WoDeltaBuildOptions(BaselineSubProjectId: subProjectId!, TargetMode: WoDeltaTargetMode.Normal),
                ctx.CancellationToken);

            _log.LogInformation(
                "ADHOC_SINGLE_STAGE_END RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} Stage={Stage} Outcome={Outcome} DataAreaId={DataAreaId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid} WorkOrderCount={WorkOrderCount}",
                runId, correlationId, sourceSystem, "AdHocSingle", TelemetryConventions.Stages.DeltaBuildEnd, delta.TotalDeltaLines > 0 ? TelemetryConventions.Outcomes.Success : TelemetryConventions.Outcomes.Skipped, company, requestWorkOrderId, woGuid, delta.WorkOrdersInOutput);

            List<PostResult> postResults = new();

            if (!string.IsNullOrWhiteSpace(delta.DeltaPayloadJson) && delta.TotalDeltaLines > 0)
            {
                _log.LogInformation(
                    "ADHOC_SINGLE_STAGE_BEGIN RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} Stage={Stage} Outcome={Outcome} DataAreaId={DataAreaId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid}",
                    runId, correlationId, sourceSystem, "AdHocSingle", "ValidateAndPost.Begin", TelemetryConventions.Outcomes.Accepted, company, requestWorkOrderId, woGuid);

                postResults = await _posting.ValidateOnceAndPostAllJournalTypesAsync(runCtx, delta.DeltaPayloadJson!, ctx.CancellationToken);

                _log.LogInformation(
                    "ADHOC_SINGLE_STAGE_END RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} Stage={Stage} Outcome={Outcome} DataAreaId={DataAreaId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid} SucceededCount={SucceededCount} FailedCount={FailedCount} RetryableCount={RetryableCount}",
                    runId, correlationId, sourceSystem, "AdHocSingle", "ValidateAndPost.End",
                    postResults.All(r => r.IsSuccess) ? TelemetryConventions.Outcomes.Success : (postResults.Any(r => r.IsSuccess) ? TelemetryConventions.Outcomes.Partial : TelemetryConventions.Outcomes.Failed),
                    company, requestWorkOrderId, woGuid, postResults.Count(r => r.IsSuccess), postResults.Count(r => !r.IsSuccess), postResults.Sum(r => r.RetryableWorkOrders));
            }

            var allOk = postResults.Count == 0 || postResults.All(r => r.IsSuccess);

            if (allOk)
                await _docCopy.CopyAsync(runCtx, woGuid, company!, subProjectId!, ctx.CancellationToken);

            object? invoiceAttributesUpdate = null;
            if (allOk)
            {
                var enrich = await _invoiceSync.EnrichPostingPayloadAsync(runCtx, payload.PayloadJson, ctx.CancellationToken);
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

            if (delta.TotalDeltaLines == 0 || string.IsNullOrWhiteSpace(delta.DeltaPayloadJson))
            {
                _log.LogInformation(
                    "WO_PROCESS_COMPLETED RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} DataAreaId={DataAreaId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid} Stage={Stage} Outcome={Outcome} CandidateLineCount={CandidateLineCount} PostedLineCount={PostedLineCount} ErrorCount={ErrorCount} ElapsedMs={ElapsedMs}",
                    runId, correlationId, sourceSystem, "AdHocSingle", company, requestWorkOrderId, woGuid, TelemetryConventions.Stages.Completed, TelemetryConventions.Outcomes.Skipped, delta.TotalDeltaLines, 0, 0, started.ElapsedMilliseconds);

                return await OkAsync(req, correlationId, runId, new
                {
                    runId,
                    correlationId,
                    sourceSystem,
                    workOrderGuid = woGuid,
                    workOrderNumbers = payload.WorkOrderNumbers,
                    message = "No deltas detected; nothing to post.",
                    delta = new
                    {
                        delta.WorkOrdersInInput,
                        delta.WorkOrdersInOutput,
                        delta.TotalDeltaLines,
                        delta.TotalReverseLines,
                        delta.TotalRecreateLines
                    },
                    invoiceAttributesUpdate
                });
            }

            var overallOutcome =
                postResults.Count == 0 || postResults.All(r => r.IsSuccess)
                    ? TelemetryConventions.Outcomes.Success
                    : (postResults.Any(r => r.IsSuccess)
                        ? TelemetryConventions.Outcomes.Partial
                        : TelemetryConventions.Outcomes.Failed);

            var postedWorkOrderCount = postResults.Sum(r => r.WorkOrdersPosted);
            var errorCount = postResults.Sum(r => r.Errors?.Count ?? 0);

            _log.LogInformation(
                "WO_PROCESS_COMPLETED RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} DataAreaId={DataAreaId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid} Stage={Stage} Outcome={Outcome} CandidateLineCount={CandidateLineCount} PostedLineCount={PostedLineCount} ErrorCount={ErrorCount} ElapsedMs={ElapsedMs}",
                runId, correlationId, sourceSystem, "AdHocSingle", company, requestWorkOrderId, woGuid, TelemetryConventions.Stages.Completed, overallOutcome, delta.TotalDeltaLines, postedWorkOrderCount, errorCount, started.ElapsedMilliseconds);

            return await OkAsync(req, correlationId, runId, new
            {
                runId,
                correlationId,
                sourceSystem,
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
                invoiceAttributesUpdate
            });
        }
        finally
        {
            if (_adHocSingleInFlight.TryRemove(singleFlightKey, out var ownerRunId))
            {
                _log.LogInformation(
                    "ADHOC_SINGLE_LOCK_RELEASED RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} DataAreaId={DataAreaId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid} LockKey={LockKey} OwnerRunId={OwnerRunId}",
                    runId, correlationId, sourceSystem, "AdHocSingle", requestCompany, requestWorkOrderId, woGuid, singleFlightKey, ownerRunId);
            }
        }
    }

    private static string? TryExtractWorkOrderId(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("_request", out var req) || req.ValueKind != JsonValueKind.Object)
                return null;
            if (!req.TryGetProperty("WOList", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
                return null;
            var first = list[0];
            if (first.ValueKind != JsonValueKind.Object)
                return null;
            if (first.TryGetProperty("WorkOrderID", out var id) && id.ValueKind == JsonValueKind.String)
                return id.GetString();
            if (first.TryGetProperty("WorkOrderId", out var id2) && id2.ValueKind == JsonValueKind.String)
                return id2.GetString();
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryExtractBusinessEventLegalEntity(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("BusinessEventLegalEntity", out var legalEntity) &&
                legalEntity.ValueKind == JsonValueKind.String)
            {
                var value = legalEntity.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            if (doc.RootElement.TryGetProperty("_request", out var req) &&
                req.ValueKind == JsonValueKind.Object &&
                req.TryGetProperty("WOList", out var list) &&
                list.ValueKind == JsonValueKind.Array &&
                list.GetArrayLength() > 0)
            {
                var first = list[0];
                if (first.ValueKind == JsonValueKind.Object &&
                    first.TryGetProperty("Company", out var company) &&
                    company.ValueKind == JsonValueKind.String)
                {
                    var value = company.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
