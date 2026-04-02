using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace Rpc.AIS.Accrual.Orchestrator.Application.Features.Delta.FsaDeltaPayload.Services.EnrichmentPipeline;

/// <summary>
/// OCP-friendly enrichment pipeline: steps are discovered via DI.
/// </summary>
public sealed class DefaultFsaDeltaPayloadEnrichmentPipeline : IFsaDeltaPayloadEnrichmentPipeline
{
    private readonly IReadOnlyList<IFsaDeltaPayloadEnrichmentStep> _steps;
    private readonly ILogger<DefaultFsaDeltaPayloadEnrichmentPipeline> _log;

    public DefaultFsaDeltaPayloadEnrichmentPipeline(
        IEnumerable<IFsaDeltaPayloadEnrichmentStep> steps,
        ILogger<DefaultFsaDeltaPayloadEnrichmentPipeline> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _steps = (steps ?? throw new ArgumentNullException(nameof(steps)))
            .OrderBy(s => s.Order)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<string> ApplyAsync(EnrichmentContext ctx, CancellationToken ct)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));

        var payload = ctx.PayloadJson ?? string.Empty;
        foreach (var step in _steps)
        {
            ct.ThrowIfCancellationRequested();

            var beforeLen = payload.Length;
            payload = await step.ApplyAsync(ctx with { PayloadJson = payload }, ct).ConfigureAwait(false);

            _log.LogDebug(
                "Delta payload enrichment step executed. Step={Step} Order={Order} LenBefore={Before} LenAfter={After}",
                step.Name, step.Order, beforeLen, payload?.Length ?? 0);
        }

        return payload;
    }
}
