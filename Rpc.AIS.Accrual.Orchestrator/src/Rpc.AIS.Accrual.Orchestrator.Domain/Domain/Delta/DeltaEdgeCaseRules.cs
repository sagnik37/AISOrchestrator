// File: src/Rpc.AIS.Accrual.Orchestrator.Domain/Domain/Delta/DeltaEdgeCaseRules.cs
// Extracted from DeltaCalculationEngine.cs to improve SRP.

namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;

using System;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

internal static class DeltaEdgeCaseRules
{
    internal static bool HasReversalInEffectiveReversalPeriod(
        FscmWorkOrderLineAggregation fscmAgg,
        AccountingPeriodSnapshot period,
        DateTime todayUtc)
    {
        if (fscmAgg.DateBuckets is null || fscmAgg.DateBuckets.Count == 0)
            return false;

        var effectiveStart = string.Equals(period.ClosedReversalDateStrategy, AisConstants.AccountingPeriodStrategies.EffectiveMonthFirst, StringComparison.OrdinalIgnoreCase)
            ? new DateTime(todayUtc.Year, todayUtc.Month, 1)
            : period.CurrentOpenPeriodStartDate.Date;

        foreach (var b in fscmAgg.DateBuckets)
        {
            if (!b.TransactionDate.HasValue) continue;
            var d = b.TransactionDate.Value.Date;
            if (d >= effectiveStart && b.SumQuantity < 0m)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true ONLY when one of the 5 “reversal-trigger” fields changed:
    /// Department, ProductLine, Warehouse, LineProperty, CalculatedUnitPrice.
    ///
    /// Intent-2: Price drift counts ONLY when explicitly supplied by FSA (UnitPriceProvided == true).
    /// Mixed FSCM history alone is NOT a reason to reverse.
    /// </summary>
    internal static bool RequiresReversalDueToFieldChange(
        FsaWorkOrderLineSnapshot fsa,
        FscmWorkOrderLineAggregation? fscmAgg)
    {
        if (fscmAgg is null || fscmAgg.TotalQuantity == 0m)
            return false;

        // Apply this policy only for Item journals (per  spec)
        //if (fscmAgg.JournalType != JournalType.Item)
        //    return false;

        // If we don't have dimension buckets, we cannot reliably detect attribute drift.
        // Do NOT force reversal.
        if (fscmAgg.DimensionBuckets is null || fscmAgg.DimensionBuckets.Count == 0)
            return false;

        // Normalize helper
        static string N(string? s) => DeltaGrouping.Norm(s);

        static bool Eq(string? a, string? b)
            => DeltaGrouping.Eq(N(a), N(b));

        if (HasOperationDateChange(fsa, fscmAgg))
            return true;

        // 1) Non-price field change detection:
        // If current FSA D/PL/W/LP matches ANY existing FSCM dimension bucket, we treat it as "no non-price field change".
        // If it matches none, then at least one of those 4 fields effectively changed (compared to posted history).
        var fsaDept = N(fsa.Department);
        var fsaPl = N(fsa.ProductLine);
        var fsaWh = N(fsa.Warehouse);
        var fsaLp = N(fsa.LineProperty);
        var fsaCustProdId = N(fsa.CustomerProductId);
        var fsaCustProdDesc = N(fsa.CustomerProductDescription);
        var fsaTaxability = N(fsa.TaxabilityType);

        bool matchesAnyBucketOnNonPrice = false;

        foreach (var b in fscmAgg.DimensionBuckets)
        {
            if (Eq(b.Department, fsaDept) &&
                Eq(b.ProductLine, fsaPl) &&
                Eq(b.Warehouse, fsaWh) &&
                Eq(b.LineProperty, fsaLp) &&
                Eq(b.CustomerProductId, fsaCustProdId) &&
                Eq(b.CustomerProductDescription, fsaCustProdDesc) &&
                Eq(b.TaxabilityType, fsaTaxability))
            {
                matchesAnyBucketOnNonPrice = true;
                break;
            }
        }

        var nonPriceFieldChanged = !matchesAnyBucketOnNonPrice;

        if (nonPriceFieldChanged)
            return true;

        // 2) Price change detection (Intent-2):
        // Compare ONLY when FSA explicitly supplied a unit price (UnitPriceProvided == true).
        if (!fsa.UnitPriceProvided)
            return false;

        if (!fsa.CalculatedUnitPrice.HasValue)
            return false;

        // Compare against the matching bucket(s). If any matching bucket has the same price => no price change.
        // If matching buckets exist but none have price, we do NOT force reversal (conservative).
        bool sawComparablePrice = false;

        foreach (var b in fscmAgg.DimensionBuckets)
        {
            if (!Eq(b.Department, fsaDept) ||
                !Eq(b.ProductLine, fsaPl) ||
                !Eq(b.Warehouse, fsaWh) ||
                !Eq(b.LineProperty, fsaLp) ||
                !Eq(b.CustomerProductId, fsaCustProdId) ||
                !Eq(b.CustomerProductDescription, fsaCustProdDesc) ||
                !Eq(b.TaxabilityType, fsaTaxability))
            {
                continue;
            }

            if (!b.CalculatedUnitPrice.HasValue)
                continue;

            sawComparablePrice = true;

            if (DeltaGrouping.PriceEq(b.CalculatedUnitPrice.Value, fsa.CalculatedUnitPrice.Value))
                return false; // explicit price matches at least one posted bucket => not a field-change trigger
        }

        // If we had comparable prices and none matched, then explicit price changed.
        if (sawComparablePrice)
            return true;

        // No comparable FSCM price present in matching buckets -> don't force reversal
        return false;
    }

    private static bool HasOperationDateChange(
        FsaWorkOrderLineSnapshot fsa,
        FscmWorkOrderLineAggregation fscmAgg)
    {
        var fsaOperationsDate = fsa.OperationsDateUtc?.Date;
        if (!fsaOperationsDate.HasValue)
            return false;

        var representativeDate =
            fscmAgg.RepresentativeSnapshot?.OperationDate?.Date
            ?? fscmAgg.RepresentativeSnapshot?.TransactionDate?.Date;

        if (representativeDate.HasValue && representativeDate.Value == fsaOperationsDate.Value)
            return false;

        if (fscmAgg.DateBuckets is null || fscmAgg.DateBuckets.Count == 0)
            return representativeDate.HasValue;

        foreach (var bucket in fscmAgg.DateBuckets)
        {
            if (bucket.TransactionDate.HasValue && bucket.TransactionDate.Value.Date == fsaOperationsDate.Value)
                return false;
        }

        return representativeDate.HasValue || fscmAgg.TotalQuantity != 0m;
    }
}
