using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Functions.Services;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Durable orchestrations for job operations (Post / Customer Change).
/// These orchestrations intentionally reuse the existing "Fetch from FSA -> optional delta -> validate/post" pipeline,
/// then execute operation-specific FSCM project lifecycle steps.
/// </summary>
public sealed class JobOperationsOrchestration
{
    public JobOperationsOrchestration()
    {
    }


    public sealed record JobOperationInputDto(
        string RunId,
        string CorrelationId,
        string TriggeredBy,
        string? SourceSystem,
        Guid WorkOrderGuid,
        string? RawRequestJson);

    public sealed record JobOperationOutcomeDto(
        bool IsSuccess,
        string RunId,
        string CorrelationId,
        string TriggeredBy,
        string? SourceSystem,
        Guid WorkOrderGuid,
        string? DurableInstanceId,
        string? Notes);

    [Function(nameof(PostJobOrchestrator))]
    public async Task<JobOperationOutcomeDto> PostJobOrchestrator([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<JobOperationInputDto>()
            ?? throw new InvalidOperationException("Orchestration input is required.");

        var log = context.CreateReplaySafeLogger(nameof(JobOperationsOrchestration));
        log.LogInformation(
            "JobOp.Begin Operation=Post RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} WorkOrderGuid={WorkOrderGuid} Stage={Stage} Outcome={Outcome}",
            input.RunId, input.CorrelationId, input.SourceSystem, input.WorkOrderGuid, "Begin", TelemetryConventions.Outcomes.Accepted);

        // Reuse existing durable pipeline, but scoped to one Work Order.
        await RunAccrualPipelineForSingleWoAsync(context, input, log);

        // Operation-specific FSCM project lifecycle steps (placeholders).
        await context.CallActivityAsync(
            nameof(JobOperationsActivities.UpdateProjectStage),
            new JobOperationsActivities.UpdateProjectStageInputDto(
                input.RunId, input.CorrelationId, input.SourceSystem, input.WorkOrderGuid, stage: "Ready for invoicing"));

        await context.CallActivityAsync(
            nameof(JobOperationsActivities.SyncJobAttributesToProject),
            new JobOperationsActivities.SyncJobAttributesInputDto(
                input.RunId, input.CorrelationId, input.SourceSystem, input.WorkOrderGuid));

        log.LogInformation("JobOp.End Operation=Post RunId={RunId} WorkOrderGuid={WorkOrderGuid} Stage={Stage} Outcome={Outcome}", input.RunId, input.WorkOrderGuid, "Completed", TelemetryConventions.Outcomes.Success);

        return new JobOperationOutcomeDto(
            IsSuccess: true,
            RunId: input.RunId,
            CorrelationId: input.CorrelationId,
            TriggeredBy: input.TriggeredBy,
            SourceSystem: input.SourceSystem,
            WorkOrderGuid: input.WorkOrderGuid,
            DurableInstanceId: context.InstanceId,
            Notes: "Post completed (journals + stage + attributes)." );
    }

    // ------------------------------------------------------------------
    // V2 orchestrators (additive)
    // - designed for synchronous FS-triggered flows
    // - adds runtime invoice attribute mapping + compare + update
    // ------------------------------------------------------------------

    [Function(nameof(PostJobOrchestratorV2))]
    public async Task<JobOperationOutcomeDto> PostJobOrchestratorV2([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<JobOperationInputDto>()
            ?? throw new InvalidOperationException("Orchestration input is required.");

        var log = context.CreateReplaySafeLogger(nameof(JobOperationsOrchestration));
        log.LogInformation(
            "JobOpV2.Begin Operation=Post RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} WorkOrderGuid={WorkOrderGuid}",
            input.RunId, input.CorrelationId, input.SourceSystem, input.WorkOrderGuid);

        await RunAccrualPipelineForSingleWoAsync(context, input, log);

        // Extract subprojectGuid from the raw FS request payload.
        var subprojectGuid = await context.CallActivityAsync<Guid?>(
            nameof(JobOperationsV2ParsingActivities.TryExtractSubprojectGuid),
            new JobOperationsV2ParsingActivities.ExtractSubprojectGuidInputDto(
                input.RunId, input.CorrelationId, input.SourceSystem, input.WorkOrderGuid, input.RawRequestJson ?? string.Empty));

        if (subprojectGuid is null || subprojectGuid == Guid.Empty)
        {
            log.LogWarning("JobOpV2.Post Missing subprojectGuid in request. Skipping invoice attributes + project status updates.");
            return new JobOperationOutcomeDto(true, input.RunId, input.CorrelationId, input.TriggeredBy, input.SourceSystem, input.WorkOrderGuid, context.InstanceId,
                "Posted journals; subprojectGuid missing so invoice attributes/project status were skipped.");
        }

        //await context.CallActivityAsync<JobOperationsV2Activities.UpdateInvoiceAttributesResultDto>(
        //    nameof(JobOperationsV2Activities.UpdateInvoiceAttributesRuntime),
        //    new JobOperationsV2Activities.UpdateInvoiceAttributesInputDto(
        //        input.RunId, input.CorrelationId, input.SourceSystem, input.WorkOrderGuid, subprojectGuid.Value, input.RawRequestJson ?? string.Empty));
        log.LogInformation("JobOpV2.End Operation=Post RunId={RunId} WorkOrderGuid={WorkOrderGuid}", input.RunId, input.WorkOrderGuid);

        return new JobOperationOutcomeDto(true, input.RunId, input.CorrelationId, input.TriggeredBy, input.SourceSystem, input.WorkOrderGuid, context.InstanceId,
            "V2 post completed (journals + invoice attributes + project status).");
    }



    [Function(nameof(CustomerChangeOrchestrator))]
    public async Task<JobOperationOutcomeDto> CustomerChangeOrchestrator([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<JobOperationInputDto>()
            ?? throw new InvalidOperationException("Orchestration input is required.");

        var log = context.CreateReplaySafeLogger(nameof(JobOperationsOrchestration));
        log.LogInformation(
            "JOBOP_ORCHESTRATOR_BEGIN Operation={Operation} RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} Stage={Stage} WorkOrderGuid={WorkOrderGuid} InstanceId={InstanceId}",
            "CustomerChange", input.RunId, input.CorrelationId, input.SourceSystem, input.TriggeredBy, "Begin", input.WorkOrderGuid, context.InstanceId);

        // Customer change is an end-to-end business process:
        // 1) Create new subproject, 2) post old lines into new subproject, 3) reverse old lines, 4) cancel old subproject.
        var ccResult = await context.CallActivityAsync<CustomerChangeResultDto>(
            nameof(JobOperationsActivities.CustomerChangeExecute),
            new JobOperationsActivities.CustomerChangeInputDto(
                input.RunId, input.CorrelationId, input.SourceSystem, input.WorkOrderGuid, input.RawRequestJson));

        log.LogInformation("JOBOP_ORCHESTRATOR_STAGE_END Operation={Operation} Stage={Stage} RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid} NewSubProjectId={NewSubProjectId} InstanceId={InstanceId}",
            "CustomerChange", "CustomerChangeExecute", input.RunId, input.CorrelationId, input.WorkOrderGuid, ccResult.NewSubProjectId, context.InstanceId);
        log.LogInformation("JOBOP_ORCHESTRATOR_END Operation={Operation} RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid} InstanceId={InstanceId}",
            "CustomerChange", input.RunId, input.CorrelationId, input.WorkOrderGuid, context.InstanceId);

        return new JobOperationOutcomeDto(
            IsSuccess: true,
            RunId: input.RunId,
            CorrelationId: input.CorrelationId,
            TriggeredBy: input.TriggeredBy,
            SourceSystem: input.SourceSystem,
            WorkOrderGuid: input.WorkOrderGuid,
            DurableInstanceId: context.InstanceId,
            Notes: "Customer change completed. NewSubProjectId=" + ccResult.NewSubProjectId);
    }

    private async Task RunAccrualPipelineForSingleWoAsync(TaskOrchestrationContext context, JobOperationInputDto input, ILogger log)
    {
        // 1) Get payload from Dataverse full fetch pipeline, scoped to WorkOrderGuid.
        log.LogInformation("JOBOP_PIPELINE_STAGE_BEGIN Operation={Operation} Stage={Stage} RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} WorkOrderGuid={WorkOrderGuid} InstanceId={InstanceId}",
            input.TriggeredBy, "FetchFromFsa", input.RunId, input.CorrelationId, input.SourceSystem, input.TriggeredBy, input.WorkOrderGuid, context.InstanceId);

        var delta = await context.CallActivityAsync<GetFsaDeltaPayloadResultDto>(
            nameof(FsaDeltaActivities.GetFsaDeltaPayload),
            new GetFsaDeltaPayloadInputDto(
                RunId: input.RunId,
                CorrelationId: input.CorrelationId,
                TriggeredBy: input.TriggeredBy,
                WorkOrderGuid: input.WorkOrderGuid.ToString()));

        var woPayloadJson = delta.PayloadJson ?? string.Empty;

        log.LogInformation("JOBOP_PIPELINE_STAGE_END Operation={Operation} Stage={Stage} RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid} PayloadBytes={PayloadBytes} WorkOrderCount={WorkOrderCount} InstanceId={InstanceId}",
            input.TriggeredBy, "FetchFromFsa", input.RunId, input.CorrelationId, input.WorkOrderGuid, woPayloadJson.Length, delta.WorkOrderNumbers?.Count ?? 0, context.InstanceId);

        // 1b) Always build delta-only payload by comparing FS payload to FSCM journal history.
        log.LogInformation("JOBOP_PIPELINE_STAGE_BEGIN Operation={Operation} Stage={Stage} RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} WorkOrderGuid={WorkOrderGuid} InstanceId={InstanceId}",
            input.TriggeredBy, "BuildDelta", input.RunId, input.CorrelationId, input.SourceSystem, input.TriggeredBy, input.WorkOrderGuid, context.InstanceId);

        var deltaResult = await context.CallActivityAsync<BuildDeltaPayloadFromFscmHistoryResultDto>(
            nameof(DeltaActivities.BuildDeltaPayloadFromFscmHistory),
            new BuildDeltaPayloadFromFscmHistoryInputDto(
                RunId: input.RunId,
                CorrelationId: input.CorrelationId,
                TriggeredBy: input.TriggeredBy,
                FsaPayloadJson: woPayloadJson));

        woPayloadJson = deltaResult.DeltaPayloadJson ?? string.Empty;

        log.LogInformation("JOBOP_PIPELINE_STAGE_END Operation={Operation} Stage={Stage} RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid} DeltaPayloadBytes={DeltaPayloadBytes} WorkOrdersInInput={WorkOrdersInInput} WorkOrdersInOutput={WorkOrdersInOutput} DeltaLines={DeltaLines} ReverseLines={ReverseLines} RecreateLines={RecreateLines} InstanceId={InstanceId}",
            input.TriggeredBy, "BuildDelta", input.RunId, input.CorrelationId, input.WorkOrderGuid, woPayloadJson.Length, deltaResult.WorkOrdersInInput, deltaResult.WorkOrdersInOutput, deltaResult.DeltaLines, deltaResult.ReverseLines, deltaResult.RecreateLines, context.InstanceId);

        // 2) Validate + post (existing activity)
        log.LogInformation("JOBOP_PIPELINE_STAGE_BEGIN Operation={Operation} Stage={Stage} RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} WorkOrderGuid={WorkOrderGuid} DeltaPayloadBytes={DeltaPayloadBytes} InstanceId={InstanceId}",
            input.TriggeredBy, "ValidateAndPost", input.RunId, input.CorrelationId, input.SourceSystem, input.TriggeredBy, input.WorkOrderGuid, woPayloadJson.Length, context.InstanceId);

        var postResults = await context.CallActivityAsync<List<PostResult>>(
            nameof(Activities.ValidateAndPostWoPayload),
            new DurableAccrualOrchestration.WoPayloadPostingInputDto(
                RunId: input.RunId,
                CorrelationId: input.CorrelationId,
                TriggeredBy: input.TriggeredBy,
                WoPayloadJson: woPayloadJson)) ?? new List<PostResult>();

        var failureGroups = postResults.Count(x => !x.IsSuccess);
        var successGroups = postResults.Count(x => x.IsSuccess);

        log.LogInformation(
            "JOBOP_PIPELINE_COMPLETED Operation={Operation} RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid} ResultGroups={ResultGroups} SuccessGroups={SuccessGroups} FailureGroups={FailureGroups} Stage={Stage} Outcome={Outcome} InstanceId={InstanceId}",
            input.TriggeredBy, input.RunId, input.CorrelationId, input.WorkOrderGuid, postResults.Count, successGroups, failureGroups, "Completed", failureGroups == 0 ? TelemetryConventions.Outcomes.Success : (successGroups > 0 ? TelemetryConventions.Outcomes.Partial : TelemetryConventions.Outcomes.Failed), context.InstanceId);
    }
}
