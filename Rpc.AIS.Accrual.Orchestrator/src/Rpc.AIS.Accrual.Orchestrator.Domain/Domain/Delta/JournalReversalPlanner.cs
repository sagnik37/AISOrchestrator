using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;

/// <summary>
/// Plans reversal journal lines based on existing FSCM journal history for a WorkOrder line.
/// Implements closed/open period splitting rules:
/// - If journals exist in closed period(s), reversal for those quantities must be created
///   as a separate line dated at the 1st day of the current open period.
/// - Remaining open-period journals can be reversed normally (dated "today" or caller-chosen date).
/// </summary>
public sealed class JournalReversalPlanner
{
    /// <summary>
    /// Plans reversals for ALL quantities previously journaled in FSCM for the given WorkOrder line.
    /// Caller decides later whether to also add a positive line (e.g., dimension/lineproperty/price change),
    /// or only reverse (inactive scenario).
    /// </summary>
    /// <param name="workOrderLineId">WO line GUID.</param>
    /// <param name="dateBuckets">Aggregated buckets by transaction date (may include null dates).</param>
    /// <param name="period">Period snapshot with open-period start date and closed-period classifier.</param>
    /// <param name="openPeriodReversalDate">Date to use for reversing journals from OPEN period(s) (typically "today").</param>
    /// <param name="reason">Reason string for telemetry/audit.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<ReversalPlan> PlanFullReversalAsync(
        Guid workOrderLineId,
        IReadOnlyList<FscmDateBucket> dateBuckets,
        AccountingPeriodSnapshot period,
        DateTime openPeriodReversalDate,
        string reason,
        CancellationToken ct)
    {
        if (workOrderLineId == Guid.Empty) throw new ArgumentException("WorkOrderLineId is empty.", nameof(workOrderLineId));
        if (dateBuckets is null) throw new ArgumentNullException(nameof(dateBuckets));
        if (period is null) throw new ArgumentNullException(nameof(period));
        if (string.IsNullOrWhiteSpace(reason)) reason = "Reversal";

        if (dateBuckets.Count == 0)
        {
            return new ReversalPlan(
                WorkOrderLineId: workOrderLineId,
                TotalQuantityToReverse: 0m,
                Reversals: Array.Empty<PlannedReversal>());
        }

        //  (idempotency + duplicate-reversal fix):
        // FSCM history for a WO line can already contain reversal journals (negative quantities)
        // created by previous AIS runs (e.g., ClosedPeriodSplit lines in the open period).
        //
        // The old implementation split by closed/open buckets and negated BOTH sums.
        // If open-period buckets were negative, that produced a POSITIVE "reversal" line
        // (because -openQty becomes +X), which effectively recreates quantity and can
        // lead to duplicate postings (commonly observed on Expense lines).
        //
        // For a *full reversal* we only need to bring the NET posted quantity back to zero.
        // So we compute the net quantity across all buckets and plan a SINGLE reversal line.
        // We still respect the closed-period rule by dating the reversal at
        // CurrentOpenPeriodStartDate when any closed-period activity exists.

        decimal netQty = 0m;
        var hasClosedActivity = false;

        foreach (var b in dateBuckets)
        {
            var qty = b.SumQuantity;
            if (qty == 0m) continue;

            netQty += qty;

            if (!hasClosedActivity && b.TransactionDate.HasValue)
            {
                if (await period.IsDateInClosedPeriodAsync(b.TransactionDate.Value, ct).ConfigureAwait(false))
                {
                    hasClosedActivity = true;
                }
            }
        }

        // If the net is already 0 (or net is negative due to over-reversal), do nothing.
        // Negative net implies the line has already been reversed past zero; posting more
        // would be harmful.
        if (netQty <= 0m)
        {
            return new ReversalPlan(
                WorkOrderLineId: workOrderLineId,
                TotalQuantityToReverse: 0m,
                Reversals: Array.Empty<PlannedReversal>());
        }

        var txDate = hasClosedActivity
            ? period.CurrentOpenPeriodStartDate.Date
            : openPeriodReversalDate.Date;

        var reversals = new[]
        {
            new PlannedReversal(
                TransactionDate: txDate,
                Quantity: -netQty,
                FromClosedPeriod: hasClosedActivity,
                Reason: hasClosedActivity
                    ? $"{reason} (NetFullReversal; ClosedHistoryPresent)"
                    : $"{reason} (NetFullReversal)"
            )
        };

        return new ReversalPlan(
            WorkOrderLineId: workOrderLineId,
            TotalQuantityToReverse: netQty,
            Reversals: reversals);
    }

    /// <summary>
    /// Convenience: plan reversal only for journals that match a predicate (e.g., only old dimension signature),
    /// while leaving other history untouched.
    /// This is useful if later  decide to reverse only "old signature" lines vs entire history.
    /// For  current spec, FULL reversal is used on any dimension/lineproperty/price change.
    /// </summary>
    public static async Task<ReversalPlan> PlanFilteredReversalAsync(
        Guid workOrderLineId,
        IReadOnlyList<FscmDateBucket> dateBuckets,
        AccountingPeriodSnapshot period,
        DateTime openPeriodReversalDate,
        string reason,
        Func<FscmJournalLine, bool> includeLinePredicate,
        CancellationToken ct = default)
    {
        if (includeLinePredicate is null) throw new ArgumentNullException(nameof(includeLinePredicate));

        var filteredBuckets = dateBuckets
            .Select(b =>
            {
                var lines = b.Lines?.Where(includeLinePredicate).ToList() ?? new List<FscmJournalLine>();
                var qty = lines.Sum(x => x.Quantity);
                if (qty == 0m) return null;

                decimal? ext = null;
                decimal extSum = 0m;
                bool anyExt = false;
                foreach (var l in lines)
                {
                    if (l.ExtendedAmount.HasValue)
                    {
                        extSum += l.ExtendedAmount.Value;
                        anyExt = true;
                    }
                }
                if (anyExt) ext = extSum;

                decimal? effPrice = null;
                var nonNullPrices = lines.Where(x => x.CalculatedUnitPrice.HasValue)
                    .Select(x => x.CalculatedUnitPrice!.Value).Distinct().ToList();
                if (nonNullPrices.Count == 1) effPrice = nonNullPrices[0];

                return new FscmDateBucket(
                    TransactionDate: b.TransactionDate,
                    SumQuantity: qty,
                    SumExtendedAmount: ext,
                    EffectiveUnitPrice: effPrice,
                    Lines: lines
                );
            })
            .Where(x => x is not null)
            .Cast<FscmDateBucket>()
            .ToList();

        return await PlanFullReversalAsync(
                workOrderLineId,
                filteredBuckets,
                period,
                openPeriodReversalDate,
                reason,
                ct)
            .ConfigureAwait(false);
    }
}
