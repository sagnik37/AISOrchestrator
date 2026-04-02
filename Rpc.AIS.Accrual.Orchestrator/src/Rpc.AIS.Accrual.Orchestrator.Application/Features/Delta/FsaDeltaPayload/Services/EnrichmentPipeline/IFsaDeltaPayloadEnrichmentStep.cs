using System.Threading;
using System.Threading.Tasks;

namespace Rpc.AIS.Accrual.Orchestrator.Application.Features.Delta.FsaDeltaPayload.Services.EnrichmentPipeline;

/// <summary>
/// One responsibility: apply exactly one enrichment concern to the outbound payload.
/// </summary>
public interface IFsaDeltaPayloadEnrichmentStep
{
    /// <summary>Stable identifier for ordering/logging.</summary>
    string Name { get; }

    /// <summary>Execution order (ascending). Keep gaps for easy insertion.</summary>
    int Order { get; }

    Task<string> ApplyAsync(EnrichmentContext ctx, CancellationToken ct);
}
