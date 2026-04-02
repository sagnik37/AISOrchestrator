using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services.Handlers;

public sealed partial class PostRetryableWoPayloadHandler
{
    private IDisposable BeginRetryableScope(
        RunContext runCtx,
        DurableAccrualOrchestration.RetryableWoPayloadPostingInputDto input)
        => _logger.BeginScope(new Dictionary<string, object?>
        {
            ["RunId"] = runCtx.RunId,
            ["CorrelationId"] = runCtx.CorrelationId,
            ["DurableInstanceId"] = input.DurableInstanceId,
            ["JournalType"] = input.JournalType,
            ["Attempt"] = input.Attempt,
            ["Activity"] = "PostRetryableWoPayload"
        }) ?? NoopScope.Instance;

    private void LogRetryableStart(
        DurableAccrualOrchestration.RetryableWoPayloadPostingInputDto input,
        RunContext runCtx)
    {
        _logger.LogInformation(
            "Activity PostRetryableWoPayload: Begin. RunId={RunId} CorrelationId={CorrelationId} JournalType={JournalType} Attempt={Attempt}",
            runCtx.RunId, runCtx.CorrelationId, input.JournalType, input.Attempt);
    }

    private void LogRetryableCompleted(
        DurableAccrualOrchestration.RetryableWoPayloadPostingInputDto input,
        RunContext runCtx,
        PostResult result)
    {
        _logger.LogInformation(
            "Activity PostRetryableWoPayload: Completed. RunId={RunId} CorrelationId={CorrelationId} JournalType={JournalType} Attempt={Attempt} Success={Success} PostedWO={PostedWO} RetryableWO={RetryWO}",
            runCtx.RunId, runCtx.CorrelationId, input.JournalType, input.Attempt, result.IsSuccess, result.WorkOrdersPosted, result.RetryableWorkOrders);
    }

    private void LogRetryableFailed(
        Exception ex,
        DurableAccrualOrchestration.RetryableWoPayloadPostingInputDto input,
        RunContext runCtx)
    {
        _logger.LogError(
            ex,
            "Activity PostRetryableWoPayload failed. RunId={RunId} CorrelationId={CorrelationId} JournalType={JournalType} Attempt={Attempt}",
            runCtx.RunId, runCtx.CorrelationId, input.JournalType, input.Attempt);
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        private NoopScope() { }
        public void Dispose() { }
    }
}
