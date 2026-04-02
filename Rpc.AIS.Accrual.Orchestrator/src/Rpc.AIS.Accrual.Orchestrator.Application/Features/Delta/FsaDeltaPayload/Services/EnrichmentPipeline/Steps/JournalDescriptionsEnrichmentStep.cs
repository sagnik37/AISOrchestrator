using System;
using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

namespace Rpc.AIS.Accrual.Orchestrator.Application.Features.Delta.FsaDeltaPayload.Services.EnrichmentPipeline.Steps;

public sealed class JournalDescriptionsEnrichmentStep : IFsaDeltaPayloadEnrichmentStep
{
    private readonly IFsaDeltaPayloadEnricher _enricher;

    public JournalDescriptionsEnrichmentStep(IFsaDeltaPayloadEnricher enricher)
        => _enricher = enricher ?? throw new ArgumentNullException(nameof(enricher));

    public string Name => "JournalDescriptions";
    public int Order => 600;

    public Task<string> ApplyAsync(EnrichmentContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ctx.Action))
            return Task.FromResult(ctx.PayloadJson);

        var updated = _enricher.StampJournalDescriptionsIntoPayload(ctx.PayloadJson, ctx.Action);
        return Task.FromResult(updated);
    }
}
