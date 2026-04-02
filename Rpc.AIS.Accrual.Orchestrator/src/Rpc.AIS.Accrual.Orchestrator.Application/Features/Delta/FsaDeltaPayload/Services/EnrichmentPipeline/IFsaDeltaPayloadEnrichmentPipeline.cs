using System.Threading;
using System.Threading.Tasks;

namespace Rpc.AIS.Accrual.Orchestrator.Application.Features.Delta.FsaDeltaPayload.Services.EnrichmentPipeline;

/// <summary>
/// Executes enrichment steps in a deterministic order.
/// </summary>
public interface IFsaDeltaPayloadEnrichmentPipeline
{
    Task<string> ApplyAsync(EnrichmentContext ctx, CancellationToken ct);
}
