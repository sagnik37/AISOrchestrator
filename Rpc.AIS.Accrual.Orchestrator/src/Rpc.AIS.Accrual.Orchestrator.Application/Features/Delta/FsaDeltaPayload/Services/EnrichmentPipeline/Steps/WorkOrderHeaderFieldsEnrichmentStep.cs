using System;
using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

namespace Rpc.AIS.Accrual.Orchestrator.Application.Features.Delta.FsaDeltaPayload.Services.EnrichmentPipeline.Steps;

public sealed class WorkOrderHeaderFieldsEnrichmentStep : IFsaDeltaPayloadEnrichmentStep
{
    private readonly IFsaDeltaPayloadEnricher _enricher;

    public WorkOrderHeaderFieldsEnrichmentStep(IFsaDeltaPayloadEnricher enricher)
        => _enricher = enricher ?? throw new ArgumentNullException(nameof(enricher));

    public string Name => "WorkOrderHeaderFields";
    public int Order => 500;

    public Task<string> ApplyAsync(EnrichmentContext ctx, CancellationToken ct)
    {
        if (ctx.WoIdToHeaderFields is null || ctx.WoIdToHeaderFields.Count == 0)
            return Task.FromResult(ctx.PayloadJson);

        var updated = _enricher.InjectWorkOrderHeaderFieldsIntoPayload(ctx.PayloadJson, ctx.WoIdToHeaderFields);
        return Task.FromResult(updated);
    }
}
