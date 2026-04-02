using System;
using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

namespace Rpc.AIS.Accrual.Orchestrator.Application.Features.Delta.FsaDeltaPayload.Services.EnrichmentPipeline.Steps;

public sealed class SubProjectEnrichmentStep : IFsaDeltaPayloadEnrichmentStep
{
    private readonly IFsaDeltaPayloadEnricher _enricher;

    public SubProjectEnrichmentStep(IFsaDeltaPayloadEnricher enricher)
        => _enricher = enricher ?? throw new ArgumentNullException(nameof(enricher));

    public string Name => "SubProjectId";
    public int Order => 400;

    public Task<string> ApplyAsync(EnrichmentContext ctx, CancellationToken ct)
    {
        if (ctx.WoIdToSubProjectId is null || ctx.WoIdToSubProjectId.Count == 0)
            return Task.FromResult(ctx.PayloadJson);

        var updated = _enricher.InjectSubProjectIdIntoPayload(ctx.PayloadJson, ctx.WoIdToSubProjectId);
        return Task.FromResult(updated);
    }
}
