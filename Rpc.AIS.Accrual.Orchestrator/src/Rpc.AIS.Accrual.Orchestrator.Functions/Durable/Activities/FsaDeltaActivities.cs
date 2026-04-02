// File: Rpc.AIS.Accrual.Orchestrator/src/Rpc.AIS.Accrual.Orchestrator.Functions/Functions/FsaDeltaActivities.cs
//
// - This class is now a thin Durable Activity wrapper.
// - All business logic moved to IFsaDeltaPayloadOrchestrator (see Functions/Services).
// - Behavior is preserved (same inputs/outputs, same logging scope fields).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;
using Rpc.AIS.Accrual.Orchestrator.Functions.Services;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Provides fsa delta activities behavior.
/// </summary>
public sealed class FsaDeltaActivities
{
    private readonly ILogger<FsaDeltaActivities> _log;
    private readonly IOptions<FsOptions> _ingestion;
    private readonly IFsaDeltaPayloadOrchestrator _orchestrator;

    public FsaDeltaActivities(
        ILogger<FsaDeltaActivities> log,
        IOptions<FsOptions> ingestion,
        IFsaDeltaPayloadOrchestrator orchestrator)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _ingestion = ingestion ?? throw new ArgumentNullException(nameof(ingestion));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    [Function(nameof(GetFsaDeltaPayload))]
    public async Task<GetFsaDeltaPayloadResultDto> GetFsaDeltaPayload(
        [ActivityTrigger] GetFsaDeltaPayloadInputDto input,
        FunctionContext ctx)
    {
        var runId = input.RunId;
        var corr = input.CorrelationId;

        var runCtx = new RunContext(runId, DateTimeOffset.UtcNow, input.TriggeredBy, corr);

        using var scope = LogScopes.BeginFunctionScope(_log, new LogScopeContext
        {
            Activity = nameof(GetFsaDeltaPayload),
            Operation = nameof(GetFsaDeltaPayload),
            Trigger = input.TriggeredBy,
            RunId = runId,
            CorrelationId = corr,
            SourceSystem = "AIS",
            DurableInstanceId = input.DurableInstanceId
        });


        using var triggerScope = _log.BeginScope(new Dictionary<string, object?>
        {
            ["Trigger"] = input.TriggeredBy
        });


        try
        {
            var opt = _ingestion.Value;

            if (string.IsNullOrWhiteSpace(opt.DataverseApiBaseUrl))
                throw new InvalidOperationException("FsaIngestion:DataverseApiBaseUrl is missing.");

            _log.LogInformation(
                "GetFsaDeltaPayload START PageSize={PageSize} MaxPages={MaxPages} Stage={Stage} Outcome={Outcome} SourceSystem={SourceSystem}",
                opt.PageSize, opt.MaxPages, TelemetryConventions.Stages.FetchFromFsaBegin, TelemetryConventions.Outcomes.Accepted, input.TriggeredBy);

            // AIS ingests open Work Orders via Full Fetch only.
            var result = await _orchestrator.BuildFullFetchAsync(input, opt, ctx.CancellationToken);

            _log.LogInformation(
                "GetFsaDeltaPayload END Stage={Stage} Outcome={Outcome} SourceSystem={SourceSystem} WorkOrderCount={WorkOrderCount} PayloadBytes={PayloadBytes}",
                TelemetryConventions.Stages.FetchFromFsaEnd,
                result is null || string.IsNullOrWhiteSpace(result.PayloadJson) || result.WorkOrderNumbers.Count == 0 ? TelemetryConventions.Outcomes.Skipped : TelemetryConventions.Outcomes.Success,
                input.TriggeredBy,
                result?.WorkOrderNumbers?.Count ?? 0,
                result?.PayloadJson?.Length ?? 0);

            return result;
        }
        catch (Exception ex)
        {
_log.LogError(ex, "GetFsaDeltaPayload FAILED Stage={Stage} Outcome={Outcome} FailureCategory={FailureCategory} ErrorType={ErrorType} SourceSystem={SourceSystem}", TelemetryConventions.Stages.FetchFromFsaEnd, TelemetryConventions.Outcomes.Failed, TelemetryConventions.ClassifyFailure(ex), ex.GetType().Name, input.TriggeredBy);
            throw;
        }
    }
}
