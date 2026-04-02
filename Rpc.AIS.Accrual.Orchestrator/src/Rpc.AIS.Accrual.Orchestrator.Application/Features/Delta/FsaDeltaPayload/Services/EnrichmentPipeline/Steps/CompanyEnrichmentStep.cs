using System;
using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

namespace Rpc.AIS.Accrual.Orchestrator.Application.Features.Delta.FsaDeltaPayload.Services.EnrichmentPipeline.Steps;

public sealed class CompanyEnrichmentStep : IFsaDeltaPayloadEnrichmentStep
{
    private readonly IFsaDeltaPayloadEnricher _enricher;

    public CompanyEnrichmentStep(IFsaDeltaPayloadEnricher enricher)
        => _enricher = enricher ?? throw new ArgumentNullException(nameof(enricher));

    public string Name => "Company";
    public int Order => 200;

    public Task<string> ApplyAsync(EnrichmentContext ctx, CancellationToken ct)
    {
        if (ctx.WoIdToCompanyName is null || ctx.WoIdToCompanyName.Count == 0)
            return Task.FromResult(ctx.PayloadJson);

        var updated = _enricher.InjectCompanyIntoPayload(ctx.PayloadJson, ctx.WoIdToCompanyName);
        return Task.FromResult(updated);
    }
}
