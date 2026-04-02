using System;
using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

namespace Rpc.AIS.Accrual.Orchestrator.Application.Features.Delta.FsaDeltaPayload.Services.EnrichmentPipeline.Steps;

public sealed class JournalNamesEnrichmentStep : IFsaDeltaPayloadEnrichmentStep
{
    private readonly IFsaDeltaPayloadEnricher _enricher;

    public JournalNamesEnrichmentStep(IFsaDeltaPayloadEnricher enricher)
        => _enricher = enricher ?? throw new ArgumentNullException(nameof(enricher));

    public string Name => "JournalNames";
    public int Order => 300;

    public Task<string> ApplyAsync(EnrichmentContext ctx, CancellationToken ct)
    {
        if (ctx.JournalNamesByCompany is null || ctx.JournalNamesByCompany.Count == 0)
            return Task.FromResult(ctx.PayloadJson);

        var updated = _enricher.InjectJournalNamesIntoPayload(ctx.PayloadJson, ctx.JournalNamesByCompany);
        return Task.FromResult(updated);
    }
}
