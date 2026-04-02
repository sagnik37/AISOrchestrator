// File: .../Core/Domain/Delta/DeltaBucketBuilder.cs


namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

internal static class DeltaBucketBuilder
{
    internal static IReadOnlyList<DeltaPlannedLine> BuildReversalLinesOnly(
        FsaWorkOrderLineSnapshot fsa,
        FscmWorkOrderLineAggregation fscmAgg,
        ReversalPlan plan)
    {
        if (plan.Reversals.Count == 0)
            return Array.Empty<DeltaPlannedLine>();

        var hist = GetFscmHistoryAttributes(fscmAgg);

        var list = new List<DeltaPlannedLine>(plan.Reversals.Count);
        foreach (var r in plan.Reversals)
        {
            // IMPORTANT:
            // Reversal lines must ALWAYS be based on FSCM history amounts (never FS).
            // If FSCM cannot provide a unit price, leave it null and let downstream validation
            // surface a clear error rather than silently falling back to FS.
            var unitPrice = ResolveFscmUnitPriceForReversal(fscmAgg, hist.UnitPrice);

            list.Add(new DeltaPlannedLine(
                TransactionDate: r.TransactionDate.Date,
                Quantity: r.Quantity,
                CalculatedUnitPrice: unitPrice,
                ExtendedAmount: ComputeExtendedAmount(r.Quantity, unitPrice),
                LineProperty: hist.LineProperty ?? fsa.LineProperty,
                Department: hist.Department ?? fsa.Department,
                ProductLine: hist.ProductLine ?? fsa.ProductLine,
                Warehouse: hist.Warehouse ?? fsa.Warehouse,
                IsReversal: true,
                FromClosedPeriodSplit: r.FromClosedPeriod,
                LineReason: r.Reason,
                ReversalPayloadSnapshotOverride: fscmAgg.RepresentativeSnapshot
            ));
        }
        return list;
    }
    internal static DeltaPlannedLine BuildReversalLine(
        FsaWorkOrderLineSnapshot fsa,
        FscmWorkOrderLineAggregation fscmAgg,
        DateTime transactionDate,
        decimal quantity,
        bool fromClosedSplit,
        string lineReason)
    {
        var hist = GetFscmHistoryAttributes(fscmAgg);

        // IMPORTANT:
        // Reversal lines must ALWAYS be based on FSCM history amounts (never FS).
        // If FSCM cannot provide a unit price, leave it null and let downstream validation
        // surface a clear error rather than silently falling back to FS.
        var unitPrice = ResolveFscmUnitPriceForReversal(fscmAgg, hist.UnitPrice);

        return new DeltaPlannedLine(
            TransactionDate: transactionDate.Date,
            Quantity: quantity,
            CalculatedUnitPrice: unitPrice,
            ExtendedAmount: ComputeExtendedAmount(quantity, unitPrice),
            LineProperty: hist.LineProperty ?? fsa.LineProperty,
            Department: hist.Department ?? fsa.Department,
            ProductLine: hist.ProductLine ?? fsa.ProductLine,
            Warehouse: fsa.Warehouse,
            IsReversal: true,
            FromClosedPeriodSplit: fromClosedSplit,
            LineReason: lineReason,
            ReversalPayloadSnapshotOverride: fscmAgg.RepresentativeSnapshot
        );
    }
    internal static DeltaPlannedLine BuildPositiveLine(
        FsaWorkOrderLineSnapshot fsa,
        DateTime transactionDate,
        decimal quantity,
        bool isReversal,
        bool fromClosedSplit,
        string lineReason)
    {
        var unitPrice = fsa.CalculatedUnitPrice;

        return new DeltaPlannedLine(
            TransactionDate: transactionDate.Date,
            Quantity: quantity,
            CalculatedUnitPrice: unitPrice,
            ExtendedAmount: ComputeExtendedAmount(quantity, unitPrice),
            LineProperty: fsa.LineProperty,
            Department: fsa.Department,
            ProductLine: fsa.ProductLine,
            Warehouse: fsa.Warehouse,
            IsReversal: isReversal,
            FromClosedPeriodSplit: fromClosedSplit,
            LineReason: lineReason,
            ReversalPayloadSnapshotOverride: null
        );
    }
    internal static decimal? ComputeExtendedAmount(decimal quantity, decimal? unitPrice)
    {
        if (!unitPrice.HasValue) return null;
        return quantity * unitPrice.Value;
    }



    internal static IReadOnlyList<DeltaPlannedLine> BuildReversalLinesByProfile(
        FsaWorkOrderLineSnapshot fsa,
        IReadOnlyList<ReversalProfilePlanner.PlannedProfileReversal> plannedReversals)
    {
        if (plannedReversals is null || plannedReversals.Count == 0)
            return Array.Empty<DeltaPlannedLine>();

        var list = new List<DeltaPlannedLine>(plannedReversals.Count);
        foreach (var planned in plannedReversals)
        {
            var profile = planned.Profile;
            var reversal = planned.Reversal;
            var unitPrice = ResolveProfileUnitPriceForReversal(profile);

            list.Add(new DeltaPlannedLine(
                TransactionDate: reversal.TransactionDate.Date,
                Quantity: reversal.Quantity,
                CalculatedUnitPrice: unitPrice,
                ExtendedAmount: ComputeExtendedAmount(reversal.Quantity, unitPrice),
                LineProperty: profile.LineProperty ?? fsa.LineProperty,
                Department: profile.Department ?? fsa.Department,
                ProductLine: profile.ProductLine ?? fsa.ProductLine,
                Warehouse: profile.Warehouse ?? fsa.Warehouse,
                IsReversal: true,
                FromClosedPeriodSplit: reversal.FromClosedPeriod,
                LineReason: reversal.Reason,
                ReversalPayloadSnapshotOverride: profile.RepresentativeSnapshot));
        }

        return list;
    }
    private static decimal? ResolveFscmUnitPriceForReversal(FscmWorkOrderLineAggregation fscmAgg, decimal? histUnitPrice)
    {
        // Prefer dimension-bucket price (most specific), then FSCM effective unit price,
        // finally compute from totals if available.
        if (histUnitPrice.HasValue)
            return histUnitPrice.Value;

        if (fscmAgg.EffectiveUnitPrice.HasValue)
            return fscmAgg.EffectiveUnitPrice.Value;

        if (fscmAgg.TotalExtendedAmount.HasValue && fscmAgg.TotalQuantity != 0m)
            return fscmAgg.TotalExtendedAmount.Value / fscmAgg.TotalQuantity;

        return null;
    }

    private static decimal? ResolveProfileUnitPriceForReversal(FscmReversalProfile profile)
    {
        if (profile.CalculatedUnitPrice.HasValue)
            return profile.CalculatedUnitPrice.Value;

        if (profile.NetExtendedAmount.HasValue && profile.NetQuantity != 0m)
            return profile.NetExtendedAmount.Value / profile.NetQuantity;

        return null;
    }

    private static (string? Department, string? ProductLine, string? Warehouse, string? LineProperty, decimal? UnitPrice) GetFscmHistoryAttributes(FscmWorkOrderLineAggregation fscmAgg)
    {
        if (fscmAgg.DimensionBuckets is { Count: > 0 })
        {
            var b = fscmAgg.DimensionBuckets[0];
            return (b.Department, b.ProductLine, b.Warehouse, b.LineProperty, b.CalculatedUnitPrice);
        }

        return (null, null, null, null, fscmAgg.EffectiveUnitPrice);
    }
}
