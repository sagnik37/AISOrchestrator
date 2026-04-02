using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Provides sub project function behavior.
/// </summary>
public sealed class SubProjectFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SubProjectProvisioningService _service;
    private readonly IRunIdGenerator _runIds;
    private readonly IAisLogger _ais;
    private readonly ILogger<SubProjectFunction> _log;

    public SubProjectFunction(
        SubProjectProvisioningService service,
        IRunIdGenerator runIds,
        IAisLogger ais,
        ILogger<SubProjectFunction> log)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _runIds = runIds ?? throw new ArgumentNullException(nameof(runIds));
        _ais = ais ?? throw new ArgumentNullException(nameof(ais));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Carries sub project envelope data.
    /// </summary>
    public sealed record SubProjectEnvelope(SubProjectCreateRequest? _request);

    /// <summary>
    /// Carries legacy sub project request data.
    /// </summary>
    public sealed record LegacySubProjectRequest(
        string LegalEntity,
        string ParentProjectId,
        string WorkOrderId,
        string? Name);

    [Function("FSCM_CreateSubProject")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "fscm/subproject")] HttpRequestData req,
        FunctionContext ctx)
    {
        var correlationId =
            (req.Headers.TryGetValues("x-correlation-id", out var corrValues) ? corrValues.FirstOrDefault() : null)
            ?? Guid.NewGuid().ToString("N");

        var runId =
            (req.Headers.TryGetValues("x-run-id", out var runValues) ? runValues.FirstOrDefault() : null)
            ?? _runIds.NewRunId();

        var sourceSystem = "FS";
        var runContext = new RunContext(runId, DateTimeOffset.UtcNow, "SubProject", correlationId, sourceSystem);

        using var _ = LogScopes.BeginFunctionScope(_log, new LogScopeContext
        {
            Function = "FSCM_CreateSubProject",
            Operation = "FSCM_CreateSubProject",
            FlowName = "SubProject",
            Trigger = "Http",
            TriggerName = "SubProject",
            TriggerChannel = "Http",
            InitiatedBy = "FS",
            RunId = runId,
            CorrelationId = correlationId,
            SourceSystem = sourceSystem,
            Stage = "Inbound"
        });

        _log.LogInformation(
            "SubProject.Start RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} InitiatedBy={InitiatedBy} Stage={Stage} Method={Method} Url={Url}",
            runId, correlationId, sourceSystem, "SubProject", "FS", "Inbound", req.Method, req.Url);

        await _ais.InfoAsync(runId, "SubProject", "HTTP request received for subproject creation.", new
        {
            correlationId
        }, ctx.CancellationToken);

        string bodyText;
        using (var reader = new StreamReader(req.Body))
        {
            bodyText = await reader.ReadToEndAsync(ctx.CancellationToken);
        }

        _log.LogInformation(
            "SubProject.BodyReceived RunId={RunId} CorrelationId={CorrelationId} BodyInfo={@BodyInfo}",
            runId, correlationId, PayloadInfo(bodyText));

        SubProjectCreateRequest? payload = null;

        try
        {
            var env = JsonSerializer.Deserialize<SubProjectEnvelope>(bodyText, JsonOptions);
            payload = env?._request;

            if (payload is not null)
            {
                payload = NormalizeSubProjectRequest(payload);
            }

            if (payload is null)
            {
                var legacy = JsonSerializer.Deserialize<LegacySubProjectRequest>(bodyText, JsonOptions);
                if (legacy is not null)
                {
                    payload = new SubProjectCreateRequest(
                        DataAreaId: legacy.LegalEntity,
                        ParentProjectId: legacy.ParentProjectId,
                        ProjectName: legacy.Name ?? $"WO-SubProject-{legacy.WorkOrderId}",
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
                        ProjectStatus: (int?)FscmSubProjectStatus.Inprocess)
                    {
                        WorkOrderId = legacy.WorkOrderId
                    };
                }
            }
        }
        catch (JsonException je)
        {
            await _ais.WarnAsync(runId, "SubProject", "Invalid JSON payload received.", new
            {
                correlationId,
                Error = je.Message
            }, ctx.CancellationToken);

            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new
            {
                status = "Error",
                message = "Invalid JSON payload.",
                details = je.Message
            }, cancellationToken: ctx.CancellationToken);
            return bad;
        }

        _log.LogInformation(
            "SubProject.PayloadParsed RunId={RunId} CorrelationId={CorrelationId} UsingNewEnvelope={UsingNewEnvelope} DataAreaId={DataAreaId} ParentProjectId={ParentProjectId} WorkOrderId={WorkOrderId} ProjectName={ProjectName}",
            runId,
            correlationId,
            bodyText.Contains("\"_request\"", StringComparison.OrdinalIgnoreCase),
            payload?.DataAreaId,
            payload?.ParentProjectId,
            payload?.WorkOrderId,
            payload?.ProjectName);

        if (payload is null)
        {
            await _ais.WarnAsync(runId, "SubProject", "Missing payload.", new { correlationId }, ctx.CancellationToken);

            var bad = req.CreateResponse(HttpStatusCode.BadRequest);

            try
            {
                await bad.WriteAsJsonAsync(new
                {
                    status = "Error",
                    message = "Payload is missing."
                }, cancellationToken: CancellationToken.None);
            }
            catch (TaskCanceledException ex)
            {
                await _ais.WarnAsync(runId, "SubProject", "Response write canceled (BadRequest).", new { correlationId, ex.Message }, CancellationToken.None);
            }
            catch (OperationCanceledException ex)
            {
                await _ais.WarnAsync(runId, "SubProject", "Response write aborted (BadRequest).", new { correlationId, ex.Message }, CancellationToken.None);
            }

            return bad;
        }

        var sw = Stopwatch.StartNew();
        _log.LogInformation(
            "SubProject.Step.Begin Step={Step} RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} Stage={Stage}",
            "CreateSubProject.Begin", runId, correlationId, sourceSystem, "SubProject", "CreateSubProject.Begin");

        var result = await _service.ProvisionAsync(runContext, payload, ctx.CancellationToken);

        sw.Stop();
        _log.LogInformation(
            "SubProject.Step.End Step={Step} RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} Outcome={Outcome} ElapsedMs={ElapsedMs} Success={Success} Message={Message} ErrorCount={ErrorCount} SubProjectId={SubProjectId}",
            "ProvisionAsync",
            runId,
            correlationId,
            sourceSystem,
            "SubProject",
            result.IsSuccess ? "Success" : "Failed",
            sw.ElapsedMilliseconds,
            result.IsSuccess,
            result.Message,
            result.Errors?.Count ?? 0,
            result.parmSubProjectId);

        if (result.IsSuccess)
        {
            var ok = req.CreateResponse(HttpStatusCode.OK);

            try
            {
                await ok.WriteAsJsonAsync(new
                {
                    status = "Success",
                    subProjectId = result.parmSubProjectId,
                    message = result.Message
                }, cancellationToken: CancellationToken.None);
            }
            catch (TaskCanceledException ex)
            {
                await _ais.WarnAsync(runId, "SubProject", "Response write canceled (Success).", new { correlationId, result.parmSubProjectId, ex.Message }, CancellationToken.None);
            }
            catch (OperationCanceledException ex)
            {
                await _ais.WarnAsync(runId, "SubProject", "Response write aborted (Success).", new { correlationId, result.parmSubProjectId, ex.Message }, CancellationToken.None);
            }

            return ok;
        }

        var fail = req.CreateResponse(HttpStatusCode.BadRequest);

        try
        {
            await fail.WriteAsJsonAsync(new
            {
                status = "Error",
                message = result.Message ?? "Failed to create subproject.",
                errors = result.Errors
            }, cancellationToken: CancellationToken.None);
        }
        catch (TaskCanceledException ex)
        {
            await _ais.WarnAsync(runId, "SubProject", "Response write canceled (BadRequest).",
                new { correlationId, ex.Message }, CancellationToken.None);
        }
        catch (OperationCanceledException ex)
        {
            await _ais.WarnAsync(runId, "SubProject", "Response write aborted (BadRequest).",
                new { correlationId, ex.Message }, CancellationToken.None);
        }

        return fail;
    }

    private static SubProjectCreateRequest NormalizeSubProjectRequest(SubProjectCreateRequest payload)
    {
        if (payload is null)
            throw new ArgumentNullException(nameof(payload));

        if (!string.IsNullOrWhiteSpace(payload.WorkOrderId))
            return payload;

        if (string.IsNullOrWhiteSpace(payload.ProjectName))
            return payload;

        return payload with
        {
            WorkOrderId = payload.ProjectName
        };
    }

    /// <summary>
    /// Executes sha 256 hex.
    /// </summary>
    private static string Sha256Hex(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s ?? string.Empty);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Executes payload info.
    /// </summary>
    private static object PayloadInfo(string json)
        => new
        {
            bytes = Encoding.UTF8.GetByteCount(json ?? string.Empty),
            sha256 = Sha256Hex(json ?? string.Empty)
        };
}
