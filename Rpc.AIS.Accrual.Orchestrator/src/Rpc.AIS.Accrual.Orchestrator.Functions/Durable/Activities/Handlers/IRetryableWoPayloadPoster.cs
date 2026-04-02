using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services.Handlers;

public interface IRetryableWoPayloadPoster
{
    Task<PostResult> PostAsync(
        DurableAccrualOrchestration.RetryableWoPayloadPostingInputDto input,
        RunContext runCtx,
        CancellationToken ct);
}
