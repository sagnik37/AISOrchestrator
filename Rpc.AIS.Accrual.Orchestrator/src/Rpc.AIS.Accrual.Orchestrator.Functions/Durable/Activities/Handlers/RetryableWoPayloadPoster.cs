using System;
using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services.Handlers;

public sealed class RetryableWoPayloadPoster : IRetryableWoPayloadPoster
{
    private readonly IPostingClient _posting;

    public RetryableWoPayloadPoster(IPostingClient posting)
    {
        _posting = posting ?? throw new ArgumentNullException(nameof(posting));
    }

    public Task<PostResult> PostAsync(
        DurableAccrualOrchestration.RetryableWoPayloadPostingInputDto input,
        RunContext runCtx,
        CancellationToken ct)
        => _posting.PostFromWoPayloadAsync(runCtx, input.JournalType, input.WoPayloadJson, ct);
}
