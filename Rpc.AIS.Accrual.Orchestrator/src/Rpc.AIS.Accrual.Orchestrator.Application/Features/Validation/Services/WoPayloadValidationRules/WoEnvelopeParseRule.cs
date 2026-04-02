using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.WoPayloadValidationRules;

public sealed class WoEnvelopeParseRule : IWoPayloadRule
{
    private readonly IWoEnvelopeParser _parser;

    public WoEnvelopeParseRule(IWoEnvelopeParser parser)
        => _parser = parser;

    public Task ApplyAsync(WoPayloadRuleContext ctx, CancellationToken ct)
    {
        if (ctx.Document is null)
        {
            ctx.Result = WoPayloadValidationDefaults.EmptyResult();
            ctx.StopProcessing = true;
            return Task.CompletedTask;
        }

        if (!_parser.TryGetWoList(ctx.RunContext, ctx.JournalType, ctx.Document.RootElement, out var woList, out var failure))
        {
            ctx.Result = failure ?? WoPayloadValidationDefaults.EmptyResult();
            ctx.StopProcessing = true;
            return Task.CompletedTask;
        }

        ctx.WoList = woList;
        return Task.CompletedTask;
    }
}
