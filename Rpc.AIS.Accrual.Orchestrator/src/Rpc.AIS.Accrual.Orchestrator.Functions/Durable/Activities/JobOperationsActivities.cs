using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Functions.Services;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;


namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Durable Activities for job operations.
/// </summary>
public sealed class JobOperationsActivities
{
    private readonly ILogger<JobOperationsActivities> _log;
    private readonly IFscmProjectLifecycle _projectLifecycle;
    private readonly ICustomerChangeOrchestrator _customerChange;

    public JobOperationsActivities(
        ILogger<JobOperationsActivities> log,
        IFscmProjectLifecycle projectLifecycle,
        ICustomerChangeOrchestrator customerChange)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _projectLifecycle = projectLifecycle ?? throw new ArgumentNullException(nameof(projectLifecycle));
        _customerChange = customerChange ?? throw new ArgumentNullException(nameof(customerChange));
    }

    public sealed record UpdateProjectStageInputDto(
        string RunId,
        string CorrelationId,
        string? SourceSystem,
        Guid WorkOrderGuid,
        string stage,
        string? DurableInstanceId = null);

    public sealed record SyncJobAttributesInputDto(
        string RunId,
        string CorrelationId,
        string? SourceSystem,
        Guid WorkOrderGuid,
        string? DurableInstanceId = null);

    public sealed record CustomerChangeInputDto(
        string RunId,
        string CorrelationId,
        string? SourceSystem,
        Guid WorkOrderGuid,
        string? RawRequestJson,
        string? DurableInstanceId = null);

    [Function(nameof(UpdateProjectStage))]
    public async Task UpdateProjectStage([ActivityTrigger] UpdateProjectStageInputDto input, FunctionContext ctx)
    {
        var runCtx = new RunContext(input.RunId, DateTimeOffset.UtcNow, "Durable", input.CorrelationId, input.SourceSystem);

        using var scope = LogScopes.BeginFunctionScope(_log, new LogScopeContext
        {
            Activity = nameof(UpdateProjectStage), // or nameof(SyncJobAttributesToProject), nameof(CustomerChangeExecute)
            Operation = nameof(UpdateProjectStage),
            Trigger = "Durable",
            RunId = input.RunId,
            CorrelationId = input.CorrelationId,
            SourceSystem = input.SourceSystem,
            WorkOrderGuid = input.WorkOrderGuid,
            DurableInstanceId = input.DurableInstanceId
        });
        ;


        _log.LogInformation("UpdateProjectStage START Stage={Stage}", input.stage);
        await _projectLifecycle.SetProjectStageAsync(runCtx, input.WorkOrderGuid, input.stage, ctx.CancellationToken);
        _log.LogInformation("UpdateProjectStage END Stage={Stage}", input.stage);
    }

    [Function(nameof(SyncJobAttributesToProject))]
    public async Task SyncJobAttributesToProject([ActivityTrigger] SyncJobAttributesInputDto input, FunctionContext ctx)
    {
        var runCtx = new RunContext(input.RunId, DateTimeOffset.UtcNow, "Durable", input.CorrelationId, input.SourceSystem);
        using var scope = LogScopes.BeginFunctionScope(_log, new LogScopeContext
        {
            Activity = nameof(SyncJobAttributesToProject), // or nameof(SyncJobAttributesToProject), nameof(CustomerChangeExecute)
            Operation = nameof(SyncJobAttributesToProject),
            Trigger = "Durable",
            RunId = input.RunId,
            CorrelationId = input.CorrelationId,
            SourceSystem = input.SourceSystem,
            WorkOrderGuid = input.WorkOrderGuid,
            DurableInstanceId = input.DurableInstanceId
        });


        _log.LogInformation("SyncJobAttributesToProject START");
        await _projectLifecycle.SyncJobAttributesToProjectAsync(runCtx, input.WorkOrderGuid, ctx.CancellationToken);
        _log.LogInformation("SyncJobAttributesToProject END");
    }

    [Function(nameof(CustomerChangeExecute))]
    public async Task<CustomerChangeResultDto> CustomerChangeExecute([ActivityTrigger] CustomerChangeInputDto input, FunctionContext ctx)
    {
        var runCtx = new RunContext(input.RunId, DateTimeOffset.UtcNow, "Durable", input.CorrelationId, input.SourceSystem);

        using var scope = LogScopes.BeginFunctionScope(_log, new LogScopeContext
        {
            Activity = nameof(CustomerChangeExecute), // or nameof(SyncJobAttributesToProject), nameof(CustomerChangeExecute)
            Operation = nameof(CustomerChangeExecute),
            Trigger = "Durable",
            RunId = input.RunId,
            CorrelationId = input.CorrelationId,
            SourceSystem = input.SourceSystem,
            WorkOrderGuid = input.WorkOrderGuid,
            DurableInstanceId = input.DurableInstanceId
        });



        return await _customerChange.ExecuteAsync(runCtx, input.WorkOrderGuid, input.RawRequestJson ?? "{}", ctx.CancellationToken);
    }
}
