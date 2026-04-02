using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.WoPayloadValidationRules;

public sealed class WoBuildResultRule : IWoPayloadRule
{
    private readonly IWoValidationResultBuilder _builder;

    public WoBuildResultRule(IWoValidationResultBuilder builder)
        => _builder = builder;

    public Task ApplyAsync(WoPayloadRuleContext ctx, CancellationToken ct)
    {
        ctx.Result = _builder.BuildResult(
            ctx.RunContext,
            ctx.JournalType,
            ctx.WorkOrdersBefore,
            ctx.ValidWorkOrders,
            ctx.RetryableWorkOrders,
            ctx.InvalidFailures,
            ctx.RetryableFailures,
            ctx.Stopwatch);

        ctx.StopProcessing = true;
        return Task.CompletedTask;
    }
}
