using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.WoPayloadValidationRules;

/// <summary>
/// Local (AIS-side) payload validation rule executed before calling FSCM endpoints.
/// Bridges async rule pipeline (IWoPayloadRule) to sync validator (IWoLocalValidator).
/// </summary>
public sealed class WoLocalValidationRule : IWoPayloadRule
{
    private readonly IWoLocalValidator _localValidator;

    public WoLocalValidationRule(IWoLocalValidator localValidator)
    {
        _localValidator = localValidator ?? throw new ArgumentNullException(nameof(localValidator));
    }

    public Task ApplyAsync(WoPayloadRuleContext ctx, CancellationToken ct)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));

        if (ctx.StopProcessing)
            return Task.CompletedTask;

        if (ctx.WoList.ValueKind != JsonValueKind.Array)
            return Task.CompletedTask;

        _localValidator.ValidateLocally(
            ctx.Endpoint,
            ctx.JournalType,
            ctx.WoList,
            ctx.InvalidFailures,
            ctx.RetryableFailures,
            ctx.ValidWorkOrders,
            ctx.RetryableWorkOrders,
            ct);

        return Task.CompletedTask;
    }
}
