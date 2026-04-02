// File: .../Core/Domain/Delta/DeltaMathEngine.cs
// Extracted orchestration logic from DeltaCalculationEngine.cs to improve SRP.

namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

internal static class DeltaMathEngine
{
    internal static async Task<DeltaCalculationResult> CalculateAsync(
            JournalReversalPlanner reversalPlanner,
            FsaWorkOrderLineSnapshot fsa,
            FscmWorkOrderLineAggregation? fscmAgg,
            AccountingPeriodSnapshot period,
            DateTime today,
            CancellationToken ct,
            string? reasonPrefix = null)
    {
        if (fsa is null) throw new ArgumentNullException(nameof(fsa));
        if (period is null) throw new ArgumentNullException(nameof(period));

        var reasonHead = string.IsNullOrWhiteSpace(reasonPrefix) ? "Delta" : reasonPrefix.Trim();
        var woId = fsa.WorkOrderId;
        var lineId = fsa.WorkOrderLineId;
        var opsDate = (fsa.OperationsDateUtc?.Date) ?? today.Date;
        var txDate = await period.ResolveTransactionDateUtcAsync(opsDate, ct).ConfigureAwait(false);

        var fscmTotalQty = fscmAgg?.TotalQuantity ?? 0m;

        // Business rules:
        // 1. Inactive + OPEN period => ReverseOnly
        // 2. Inactive + CLOSED period => ReverseAndRecreate (existing behavior)
        // 3. Active + significant field change => ReverseAndRecreate
        //
        // Important fix:
        // For inactive lines in an OPEN period, do NOT let field-change logic force recreate.
        // Business expectation is that deactivating a line in an open period should reverse only.
        var hasFieldChange = DeltaEdgeCaseRules.RequiresReversalDueToFieldChange(fsa, fscmAgg);
        var inactiveAndClosed = !fsa.IsActive && await period.IsDateInClosedPeriodAsync(opsDate, ct).ConfigureAwait(false);
        var inactiveAndOpen = !fsa.IsActive && !inactiveAndClosed;

        var shouldReverseAndRecreate =
            !inactiveAndOpen &&
            (hasFieldChange || inactiveAndClosed);

        // -------------------- 1) Inactive + OPEN => reverse all history only --------------------
        if (inactiveAndOpen)
        {
            if (fscmAgg is null || fscmAgg.TotalQuantity == 0m)
            {
                return new DeltaCalculationResult(
                    woId, lineId,
                    DeltaDecision.NoChange,
                    Array.Empty<DeltaPlannedLine>(),
                    $"{reasonHead}: LineInactiveOpen but no FSCM history");
            }

            if (DeltaEdgeCaseRules.HasReversalInEffectiveReversalPeriod(fscmAgg, period, opsDate))
            {
                return new DeltaCalculationResult(
                    woId, lineId,
                    DeltaDecision.NoChange,
                    Array.Empty<DeltaPlannedLine>(),
                    $"{reasonHead}: LineInactiveOpen but reversal already exists in effective period => NoChange");
            }

            var plannedProfileReversals = await ReversalProfilePlanner.PlanAsync(
                workOrderLineId: lineId,
                fscmAgg: fscmAgg,
                period: period,
                openPeriodReversalDate: txDate.Date,
                reason: $"{reasonHead}: LineInactiveOpen",
                ct: ct).ConfigureAwait(false);

            var lines = DeltaBucketBuilder.BuildReversalLinesByProfile(fsa, plannedProfileReversals);

            return new DeltaCalculationResult(
                woId, lineId,
                DeltaDecision.ReverseOnly,
                lines,
                $"{reasonHead}: LineInactiveOpen => ReverseOnly (ReversalLines={lines.Count})");
        }

        // -------------------- 2) Field change OR (inactive+closed) => reverse + recreate --------------------
        if (shouldReverseAndRecreate)
        {
            // No history => just create current FSA qty
            if (fscmAgg is null || fscmAgg.TotalQuantity == 0m)
            {
                // If the line is inactive and there is no history, there's nothing to reverse.
                if (!fsa.IsActive)
                {
                    return new DeltaCalculationResult(
                        woId, lineId,
                        DeltaDecision.NoChange,
                        Array.Empty<DeltaPlannedLine>(),
                        $"{reasonHead}: ReverseAndRecreate condition met but no FSCM history and line inactive => NoChange");
                }

                var create = DeltaBucketBuilder.BuildPositiveLine(
                    fsa: fsa,
                    transactionDate: txDate.Date,
                    quantity: fsa.Quantity,
                    isReversal: false,
                    fromClosedSplit: false,
                    lineReason: $"{reasonHead}: ReverseAndRecreate condition met but no FSCM history => Create");

                return new DeltaCalculationResult(
                    woId, lineId,
                    DeltaDecision.ReverseAndRecreate,
                    new[] { create },
                    $"{reasonHead}: ReverseAndRecreate => Create only (no history)");
            }

            // Idempotency guard: if a reversal already exists in effective period, do not reverse again.
            // With FieldChange, the safest move is to post ONLY the remaining quantity delta (if any) using current FSA attributes.
            if (DeltaEdgeCaseRules.HasReversalInEffectiveReversalPeriod(fscmAgg, period, opsDate))
            {
                var deltaQtyExisting = fsa.Quantity - fscmTotalQty;
                if (deltaQtyExisting == 0m)
                {
                    return new DeltaCalculationResult(
                        woId, lineId,
                        DeltaDecision.NoChange,
                        Array.Empty<DeltaPlannedLine>(),
                        $"{reasonHead}: ReverseAndRecreate but reversal already exists in effective period => NoChange");
                }

                var deltaExisting = DeltaBucketBuilder.BuildPositiveLine(
                    fsa: fsa,
                    transactionDate: txDate.Date,
                    quantity: deltaQtyExisting,
                    isReversal: false,
                    fromClosedSplit: false,
                    lineReason: $"{reasonHead}: QuantityDelta (idempotency-guard) (FSA={fsa.Quantity}, FSCM={fscmTotalQty})");

                return new DeltaCalculationResult(
                    woId, lineId,
                    DeltaDecision.QuantityDelta,
                    new[] { deltaExisting },
                    $"{reasonHead}: ReverseAndRecreate idempotency-guard => QuantityDelta Qty={deltaQtyExisting}");
            }

            var plannedProfileReversals = await ReversalProfilePlanner.PlanAsync(
                workOrderLineId: lineId,
                fscmAgg: fscmAgg,
                period: period,
                openPeriodReversalDate: txDate.Date,
                reason: $"{reasonHead}: ReverseAndRecreate",
                ct: ct).ConfigureAwait(false);

            var outLines = new List<DeltaPlannedLine>(capacity: plannedProfileReversals.Count + 1);

            // Reversal lines MUST use FSCM history attributes (old values)
            outLines.AddRange(DeltaBucketBuilder.BuildReversalLinesByProfile(fsa, plannedProfileReversals));

            // Recreate MUST reflect CURRENT FSA quantity with UPDATED attributes
            if (fsa.Quantity != 0m)
            {
                outLines.Add(DeltaBucketBuilder.BuildPositiveLine(
                    fsa: fsa,
                    transactionDate: txDate.Date,
                    quantity: fsa.Quantity,
                    isReversal: false,
                    fromClosedSplit: false,
                    lineReason: $"{reasonHead}: RecreateWithUpdatedAttributes (FSAQty={fsa.Quantity})"));
            }

            return new DeltaCalculationResult(
                woId, lineId,
                DeltaDecision.ReverseAndRecreate,
                outLines,
                $"{reasonHead}: ReverseAndRecreate (FieldChange={hasFieldChange}, InactiveClosed={inactiveAndClosed}) => ReversalLines={plannedProfileReversals.Count}, Recreate={(fsa.Quantity != 0m ? "Yes" : "No")}");
        }

        // -------------------- 3) Quantity delta only --------------------
        var deltaQty = fsa.Quantity - fscmTotalQty;
        if (deltaQty == 0m)
        {
            return new DeltaCalculationResult(
                woId, lineId,
                DeltaDecision.NoChange,
                Array.Empty<DeltaPlannedLine>(),
                $"{reasonHead}: NoChange (Qty equal)");
        }

        var deltaLine = DeltaBucketBuilder.BuildPositiveLine(
            fsa: fsa,
            transactionDate: txDate.Date,
            quantity: deltaQty,
            isReversal: false,
            fromClosedSplit: false,
            lineReason: $"{reasonHead}: QuantityDelta (FSA={fsa.Quantity}, FSCM={fscmTotalQty})");

        return new DeltaCalculationResult(
            woId, lineId,
            DeltaDecision.QuantityDelta,
            new[] { deltaLine },
            $"{reasonHead}: QuantityDelta => Qty={deltaQty}");
    }
}
