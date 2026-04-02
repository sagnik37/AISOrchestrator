using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

public static class LogScopes
{
    /// <summary>
    /// Preferred API: one context object.
    /// </summary>
    public static IDisposable BeginFunctionScope(ILogger logger, LogScopeContext ctx)
    {
        if (logger is null) throw new ArgumentNullException(nameof(logger));

        // Keep keys consistent across the entire solution.
        // Only emit non-null values (keeps scope small).
        var dict = new Dictionary<string, object?>(capacity: 20)
        {
            ["RunId"] = ctx.RunId,
            ["CorrelationId"] = ctx.CorrelationId,
            ["SourceSystem"] = ctx.SourceSystem,
            ["BatchId"] = ctx.BatchId,
            ["Company"] = ctx.Company,
            ["DataAreaId"] = ctx.Company,

            ["Function"] = ctx.Function,
            ["Activity"] = ctx.Activity,
            ["Operation"] = ctx.Operation,
            ["FlowName"] = ctx.FlowName,
            ["Trigger"] = ctx.Trigger,
            ["TriggerName"] = ctx.TriggerName,
            ["TriggerChannel"] = ctx.TriggerChannel,
            ["InitiatedBy"] = ctx.InitiatedBy,
            ["Step"] = ctx.Step,
            ["Stage"] = ctx.Stage,
            ["Outcome"] = ctx.Outcome,
            ["FailureCategory"] = ctx.FailureCategory,
            ["ErrorType"] = ctx.ErrorType,
            ["IsRetryable"] = ctx.IsRetryable,
            ["RetryAttempt"] = ctx.RetryAttempt,

            ["WorkOrderGuid"] = ctx.WorkOrderGuid,
            ["WorkOrderId"] = ctx.WorkOrderId,
            ["SubProjectId"] = ctx.SubProjectId,
            ["DurableInstanceId"] = ctx.DurableInstanceId,
            ["ParentInstanceId"] = ctx.ParentInstanceId,
            ["JournalType"] = ctx.JournalType?.ToString(),
            ["WorkOrderCount"] = ctx.WorkOrderCount,
            ["TotalOpenWorkOrders"] = ctx.TotalOpenWorkOrders,
            ["ProcessedCount"] = ctx.ProcessedCount,
            ["SucceededCount"] = ctx.SucceededCount,
            ["FailedCount"] = ctx.FailedCount,
            ["RemainingCount"] = ctx.RemainingCount
        };

        RemoveNulls(dict);
        return logger.BeginScope(dict);
    }

    private static void RemoveNulls(Dictionary<string, object?> dict)
    {
        // Avoid allocations from LINQ.
        // Remove null entries so AppInsights scope stays lean.
        var keysToRemove = new List<string>();

        foreach (var kvp in dict)
        {
            if (kvp.Value is null) keysToRemove.Add(kvp.Key);
        }

        for (var i = 0; i < keysToRemove.Count; i++)
        {
            dict.Remove(keysToRemove[i]);
        }
    }

    // ------------------------------------------------------------
    // OPTIONAL: keep legacy overload temporarily (migration aid)
    // ------------------------------------------------------------
    [Obsolete("Use BeginFunctionScope(ILogger, LogScopeContext).")]
    public static IDisposable BeginFunctionScope(
        ILogger logger,
        string function,
        string runId,
        string correlationId,
        string sourceSystem,
        Guid? workOrderGuid = null,
        string? trigger = null,
        string? subProjectId = null,
        string? durableInstanceId = null,
        string? workOrderId = null,
        string? step = null,
        JournalType? journalType = null)
    {
        return BeginFunctionScope(logger, new LogScopeContext
        {
            Function = function,
            Operation = function,
            RunId = runId,
            CorrelationId = correlationId,
            SourceSystem = sourceSystem,
            WorkOrderGuid = workOrderGuid,
            Trigger = trigger,
            Step = step,
            SubProjectId = subProjectId,
            DurableInstanceId = durableInstanceId,
            WorkOrderId = workOrderId,
            JournalType = journalType
        });
    }
}
