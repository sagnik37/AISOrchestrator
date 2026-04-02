using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;

/// <summary>
/// Plans reversal lines at FSCM signature level so mixed Bill/NonBill history is reversed precisely.
/// </summary>
internal static class ReversalProfilePlanner
{
    internal sealed record PlannedProfileReversal(
        FscmReversalProfile Profile,
        PlannedReversal Reversal);

    internal static async Task<IReadOnlyList<PlannedProfileReversal>> PlanAsync(
        Guid workOrderLineId,
        FscmWorkOrderLineAggregation fscmAgg,
        AccountingPeriodSnapshot period,
        DateTime openPeriodReversalDate,
        string reason,
        CancellationToken ct)
    {
        if (fscmAgg is null) throw new ArgumentNullException(nameof(fscmAgg));

        var profiles = fscmAgg.ReversalProfiles;
        if (profiles is null || profiles.Count == 0)
            return Array.Empty<PlannedProfileReversal>();

        var results = new List<PlannedProfileReversal>();
        foreach (var profile in profiles)
        {
            if (profile.NetQuantity <= 0m)
                continue;

            var plan = await JournalReversalPlanner.PlanFullReversalAsync(
                workOrderLineId: workOrderLineId,
                dateBuckets: profile.DateBuckets,
                period: period,
                openPeriodReversalDate: openPeriodReversalDate,
                reason: reason,
                ct: ct).ConfigureAwait(false);

            foreach (var reversal in plan.Reversals)
                results.Add(new PlannedProfileReversal(profile, reversal));
        }

        return results;
    }
}
