using Microsoft.Extensions.Logging;

using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Functions.Functions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services.Handlers;

public sealed partial class PostRetryableWoPayloadHandler : ActivitiesHandlerBase
{
    private readonly IRetryableWoPayloadPoster _poster;
    private readonly ILogger<PostRetryableWoPayloadHandler> _logger;

    public PostRetryableWoPayloadHandler(
        IRetryableWoPayloadPoster poster,
        ILogger<PostRetryableWoPayloadHandler> logger)
    {
        _poster = poster ?? throw new ArgumentNullException(nameof(poster));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PostResult> HandleAsync(
        DurableAccrualOrchestration.RetryableWoPayloadPostingInputDto input,
        RunContext runCtx,
        CancellationToken ct)
    {        using var scope = BeginRetryableScope(runCtx, input);

        LogRetryableStart(input, runCtx);

        try
        {
            var result = await _poster.PostAsync(input, runCtx, ct).ConfigureAwait(false);

            LogRetryableCompleted(input, runCtx, result);

            return result;
        }
        catch (Exception ex)
        {
            LogRetryableFailed(ex, input, runCtx);

            throw;
        }
    }
}
