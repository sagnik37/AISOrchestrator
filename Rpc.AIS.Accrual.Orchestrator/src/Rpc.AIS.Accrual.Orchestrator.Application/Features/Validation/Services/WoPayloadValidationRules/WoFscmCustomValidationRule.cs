using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.WoPayloadValidationRules;

public sealed class WoFscmCustomValidationRule : IWoPayloadRule
{
    private readonly IFscmReferenceValidator _validator;

    public WoFscmCustomValidationRule(IFscmReferenceValidator validator)
        => _validator = validator;

    public async Task ApplyAsync(WoPayloadRuleContext ctx, CancellationToken ct)
    {
        await _validator.ApplyFscmCustomValidationAsync(
            ctx.RunContext,
            ctx.JournalType,
            ctx.InvalidFailures,
            ctx.RetryableFailures,
            ctx.ValidWorkOrders,
            ctx.RetryableWorkOrders,
            ctx.Stopwatch,
            ct).ConfigureAwait(false);
    }
}
