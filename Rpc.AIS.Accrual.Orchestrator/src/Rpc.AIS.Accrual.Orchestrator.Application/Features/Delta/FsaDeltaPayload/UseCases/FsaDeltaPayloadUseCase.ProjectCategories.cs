// File: .../Core/UseCases/FsaDeltaPayload/FsaDeltaPayloadUseCase.ProjectCategories.cs
// Implements Project Category enrichment from FSCM CDSReleasedDistinctProducts.
// Dataverse/FS-based project category is intentionally NOT used.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.UseCases.FsaDeltaPayload;

public sealed partial class FsaDeltaPayloadUseCase
{
    private async Task<IReadOnlyList<FsaDeltaSnapshot>> EnrichReleasedDistinctProductCategoriesAsync(
        RunContext ctx,
        IReadOnlyList<FsaDeltaSnapshot> snapshots,
        CancellationToken ct)
    {
        if (snapshots is null) throw new ArgumentNullException(nameof(snapshots));
        if (snapshots.Count == 0) return snapshots;

        var itemNumbers = snapshots
            .SelectMany(s => (s.InventoryProducts ?? Array.Empty<FsaProductLine>())
                .Concat(s.NonInventoryProducts ?? Array.Empty<FsaProductLine>()))
            .Select(l => l.ItemNumber)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (itemNumbers.Count == 0)
        {
            _log.LogInformation(
                "FSCM ReleasedDistinctProducts enrichment skipped (no ItemNumbers). RunId={RunId} CorrelationId={CorrelationId}",
                ctx.RunId, ctx.CorrelationId);
            return snapshots;
        }

        _log.LogInformation(
            "FSCM ReleasedDistinctProducts enrichment START. DistinctItemNumbers={Count} RunId={RunId} CorrelationId={CorrelationId}",
            itemNumbers.Count, ctx.RunId, ctx.CorrelationId);

        var resolvedDataAreaId = ctx.DataAreaId;

        if (string.IsNullOrWhiteSpace(resolvedDataAreaId))
        {
            resolvedDataAreaId =
                snapshots
                    .SelectMany(s =>
                        (s.InventoryProducts ?? Array.Empty<FsaProductLine>())
                        .Concat(s.NonInventoryProducts ?? Array.Empty<FsaProductLine>())
                        .Select(l => l.DataAreaId)
                        .Concat((s.ServiceLines ?? Array.Empty<FsaServiceLine>()).Select(l => l.DataAreaId)))
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                    ?.Trim();
        }

        var categoryCtx = string.Equals(ctx.DataAreaId, resolvedDataAreaId, StringComparison.OrdinalIgnoreCase)
            ? ctx
            : new RunContext(
                ctx.RunId,
                DateTimeOffset.UtcNow,
                ctx.TriggeredBy,
                ctx.CorrelationId,
                ctx.SourceSystem ?? "FSCM",
                resolvedDataAreaId);

        _log.LogInformation(
            "ReleasedDistinctProducts context resolved. DataAreaId={DataAreaId} SourceSystem={SourceSystem} DistinctItemNumbers={Count} RunId={RunId} CorrelationId={CorrelationId}",
            categoryCtx.DataAreaId,
            categoryCtx.SourceSystem,
            itemNumbers.Count,
            categoryCtx.RunId,
            categoryCtx.CorrelationId);

        var map = await _releasedDistinctProducts
            .GetCategoriesByItemNumberAsync(categoryCtx, itemNumbers, ct)
            .ConfigureAwait(false);

        var hit = 0;
        var miss = 0;

        List<FsaDeltaSnapshot> updated = new(snapshots.Count);

        foreach (var s in snapshots)
        {
            var inv = EnrichLines(s.InventoryProducts, map, ref hit, ref miss);
            var nonInv = EnrichLines(s.NonInventoryProducts, map, ref hit, ref miss);

            if (ReferenceEquals(inv, s.InventoryProducts) && ReferenceEquals(nonInv, s.NonInventoryProducts))
            {
                updated.Add(s);
            }
            else
            {
                updated.Add(s with
                {
                    InventoryProducts = inv,
                    NonInventoryProducts = nonInv
                });
            }
        }

        _log.LogInformation(
            "FSCM ReleasedDistinctProducts enrichment END. Hits={Hits} Misses={Misses} MapCount={MapCount} DataAreaId={DataAreaId} SourceSystem={SourceSystem} RunId={RunId} CorrelationId={CorrelationId}",
            hit, miss, map.Count, categoryCtx.DataAreaId, categoryCtx.SourceSystem, categoryCtx.RunId, categoryCtx.CorrelationId);

        return updated;
    }

    private static IReadOnlyList<FsaProductLine> EnrichLines(
        IReadOnlyList<FsaProductLine> lines,
        IReadOnlyDictionary<string, Rpc.AIS.Accrual.Orchestrator.Core.Abstractions.ReleasedDistinctProductCategory> map,
        ref int hit,
        ref int miss)
    {
        if (lines.Count == 0) return lines;

        var changed = false;
        var list = new List<FsaProductLine>(lines.Count);

        foreach (var l in lines)
        {
            if (string.IsNullOrWhiteSpace(l.ItemNumber))
            {
                list.Add(l);
                continue;
            }

            if (map.TryGetValue(l.ItemNumber.Trim(), out var cat))
            {
                hit++;
                var newProjectCategory = cat.RpcProjCategoryId;

                if (!string.Equals(l.ProjectCategory, newProjectCategory, StringComparison.OrdinalIgnoreCase))
                {
                    changed = true;
                    list.Add(l with { ProjectCategory = newProjectCategory });
                }
                else
                {
                    list.Add(l);
                }
            }
            else
            {
                miss++;
                list.Add(l);
            }
        }

        return changed ? list : lines;
    }
}
