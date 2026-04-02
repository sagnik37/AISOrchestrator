using System.Threading;
using System.Threading.Tasks;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Pluggable rule for Work Order payload validation.
/// Rules should be deterministic and side-effect free (except logging).
/// </summary>
public interface IWoPayloadRule
{
    /// <summary>
    /// Apply the rule to the validation context.
    /// Rules may short-circuit by setting <see cref="WoPayloadRuleContext.Result"/> and <see cref="WoPayloadRuleContext.StopProcessing"/>.
    /// </summary>
    Task ApplyAsync(WoPayloadRuleContext ctx, CancellationToken ct);
}
