using System;

using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services.Handlers;

public abstract class ActivitiesHandlerBase
{
    protected static IDisposable BeginScope(ILogger logger, RunContext ctx, string activityName, string? durableInstanceId)
        => LogScopes.BeginFunctionScope(logger, new LogScopeContext
        {
            Function = activityName,
            Activity = activityName,
            Operation = ctx.TriggeredBy,
            FlowName = ctx.TriggeredBy,
            Trigger = "Durable",
            TriggerName = ctx.TriggeredBy,
            TriggerChannel = "Durable",
            InitiatedBy = ctx.SourceSystem,
            RunId = ctx.RunId,
            CorrelationId = ctx.CorrelationId,
            SourceSystem = ctx.SourceSystem,
            DurableInstanceId = durableInstanceId,
            Company = ctx.DataAreaId
        });
}
