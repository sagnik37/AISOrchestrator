using System;
using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

namespace Rpc.AIS.Accrual.Orchestrator.Application.Features.Delta.FsaDeltaPayload.Services.EnrichmentPipeline.Steps;

public sealed class FsExtrasEnrichmentStep : IFsaDeltaPayloadEnrichmentStep
{
    private readonly IFsaDeltaPayloadEnricher _enricher;

    public FsExtrasEnrichmentStep(IFsaDeltaPayloadEnricher enricher)
        => _enricher = enricher ?? throw new ArgumentNullException(nameof(enricher));

    public string Name => "FsExtras";
    public int Order => 100;

    public Task<string> ApplyAsync(EnrichmentContext ctx, CancellationToken ct)
    {
        if (ctx.ExtrasByLineGuid is null || ctx.ExtrasByLineGuid.Count == 0)
            return Task.FromResult(ctx.PayloadJson);

        var updated = _enricher.InjectFsExtrasAndLogPerWoSummary(
            ctx.PayloadJson,
            new System.Collections.Generic.Dictionary<Guid, Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.FsLineExtras>(ctx.ExtrasByLineGuid),
            ctx.RunId,
            ctx.CorrelationId);

        return Task.FromResult(updated);
    }
}
