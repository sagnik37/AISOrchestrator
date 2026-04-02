using System;
using System.Collections.Generic;
using System.Linq;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;

/// <summary>
/// Aggregates FSCM journal lines fetched for a whole WorkOrder into per WorkOrderLine aggregations.
/// The output is consumed by delta calculation and reversal planning.
/// </summary>
public sealed class FscmJournalAggregator
{
    /// <summary>
    /// Groups FSCM journal lines by WorkOrderLineId and produces an aggregation per line.
    ///
    /// Dimension grouping rules:
    /// - Warehouse is considered ONLY for Item journals.
    /// - Warehouse is ignored for Expense / Hour journals.
    /// </summary>
    public static IReadOnlyDictionary<Guid, FscmWorkOrderLineAggregation> GroupByWorkOrderLine(
        IReadOnlyList<FscmJournalLine> journalLines)
    {
        if (journalLines is null) throw new ArgumentNullException(nameof(journalLines));
        if (journalLines.Count == 0) return new Dictionary<Guid, FscmWorkOrderLineAggregation>();

        var grouped = journalLines
            .Where(x => x.WorkOrderLineId != Guid.Empty)
            .GroupBy(x => x.WorkOrderLineId);

        var dict = new Dictionary<Guid, FscmWorkOrderLineAggregation>(grouped.Count());

        foreach (var g in grouped)
        {
            var lineId = g.Key;
            var journalType = g.First().JournalType;

            // ---------------------------
            // Date Buckets
            // ---------------------------
            var byDate = g
                .GroupBy(x => x.TransactionDate?.Date)
                .Select(gg => new FscmDateBucket(
                    TransactionDate: gg.Key,
                    SumQuantity: gg.SumSafeQty(),
                    SumExtendedAmount: gg.SumSafeExtAmount(),
                    EffectiveUnitPrice: gg.EffectiveUnitPriceOrNull(),
                    Lines: gg.ToList()
                ))
                .OrderBy(b => b.TransactionDate ?? DateTime.MinValue)
                .ToList();

            // ---------------------------
            // Dimension Buckets
            // Warehouse included ONLY for Item
            // ---------------------------
            var dimBuckets = g
                .GroupBy(x => new
                {
                    Dept = x.Department?.Trim(),
                    PL = x.ProductLine?.Trim(),
                    W = journalType == JournalType.Item
                        ? x.Warehouse?.Trim()
                        : null,
                    LP = x.LineProperty?.Trim(),
                    CPID = x.CustomerProductId?.Trim(),
                    CPDESC = x.CustomerProductDescription?.Trim(),
                    TAX = x.TaxabilityType?.Trim(),
                    Price = x.CalculatedUnitPrice
                })
                .Select(gg => new FscmDimensionBucket(
                    Department: gg.Key.Dept,
                    ProductLine: gg.Key.PL,
                    Warehouse: gg.Key.W,
                    LineProperty: gg.Key.LP,
                    CustomerProductId: gg.Key.CPID,
                    CustomerProductDescription: gg.Key.CPDESC,
                    TaxabilityType: gg.Key.TAX,
                    CalculatedUnitPrice: gg.Key.Price,
                    SumQuantity: gg.SumSafeQty(),
                    SumExtendedAmount: gg.SumSafeExtAmount(),
                    TransactionDates: gg.Select(x => x.TransactionDate?.Date)
                                        .Distinct()
                                        .ToList()
                ))
                .ToList();

            // ---------------------------
            // Reversal profiles (signature-preserving)
            // ---------------------------
            var reversalProfiles = g
                .GroupBy(x => new
                {
                    Dept = x.Department?.Trim(),
                    PL = x.ProductLine?.Trim(),
                    W = journalType == JournalType.Item
                        ? x.Warehouse?.Trim()
                        : null,
                    LP = x.LineProperty?.Trim(),
                    CPID = x.CustomerProductId?.Trim(),
                    CPDESC = x.CustomerProductDescription?.Trim(),
                    TAX = x.TaxabilityType?.Trim(),
                    Price = x.CalculatedUnitPrice
                })
                .Select(gg =>
                {
                    var profileLines = gg.ToList();
                    var profileDateBuckets = profileLines
                        .GroupBy(x => x.TransactionDate?.Date)
                        .Select(bg => new FscmDateBucket(
                            TransactionDate: bg.Key,
                            SumQuantity: bg.SumSafeQty(),
                            SumExtendedAmount: bg.SumSafeExtAmount(),
                            EffectiveUnitPrice: bg.EffectiveUnitPriceOrNull(),
                            Lines: bg.ToList()))
                        .OrderBy(b => b.TransactionDate ?? DateTime.MinValue)
                        .ToList();

                    var representative = GetRepresentativeLine(profileLines, journalType)?.PayloadSnapshot;

                    return new FscmReversalProfile(
                        Department: gg.Key.Dept,
                        ProductLine: gg.Key.PL,
                        Warehouse: gg.Key.W,
                        LineProperty: gg.Key.LP,
                        CustomerProductId: gg.Key.CPID,
                        CustomerProductDescription: gg.Key.CPDESC,
                        TaxabilityType: gg.Key.TAX,
                        CalculatedUnitPrice: gg.Key.Price,
                        NetQuantity: profileLines.SumSafeQty(),
                        NetExtendedAmount: profileLines.SumSafeExtAmount(),
                        DateBuckets: profileDateBuckets,
                        RepresentativeSnapshot: representative);
                })
                .Where(x => x.NetQuantity != 0m)
                .ToList();

            // ---------------------------
            // Aggregate totals
            // ---------------------------
            var allQty = g.SumSafeQty();
            var allExt = g.SumSafeExtAmount();
            var effectivePrice = g.EffectiveUnitPriceOrNull();

            // ---------------------------
            // Dimension Signatures
            // Warehouse included ONLY for Item
            // ---------------------------
            var dimSignatures = g
                .Select(x => BuildSignature(x, journalType))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();


            // Representative snapshot used for REVERSAL payload mapping.
            // Prefer a line that has PayloadSnapshot populated; for Item journals also prefer one with Warehouse populated.
            var repLine = GetRepresentativeLine(g, journalType);
            var repSnapshot = repLine?.PayloadSnapshot;
            dict[lineId] = new FscmWorkOrderLineAggregation(
                            WorkOrderLineId: lineId,
                            WorkOrderId: g.First().WorkOrderId,
                            JournalType: journalType,
                            TotalQuantity: allQty,
                            TotalExtendedAmount: allExt,
                            EffectiveUnitPrice: effectivePrice,
                            DimensionSignatures: dimSignatures,
                            DateBuckets: byDate,
                            DimensionBuckets: dimBuckets,
                            RepresentativeSnapshot: repSnapshot,
                            ReversalProfiles: reversalProfiles
                        );
        }

        return dict;
    }


    private static FscmJournalLine? GetRepresentativeLine(IEnumerable<FscmJournalLine> lines, JournalType jt)
    {
        if (lines is null) return null;

        var list = lines as IList<FscmJournalLine> ?? lines.ToList();
        if (list.Count == 0) return null;

        // Prefer a snapshot-bearing line
        IEnumerable<FscmJournalLine> candidates = list.Where(x => x.PayloadSnapshot is not null);
        if (!candidates.Any())
            candidates = list;

        if (jt == JournalType.Item)
        {
            var withWh = candidates.Where(x => !string.IsNullOrWhiteSpace(x.PayloadSnapshot?.Warehouse) || !string.IsNullOrWhiteSpace(x.Warehouse));
            if (withWh.Any())
                candidates = withWh;
        }

        // Prefer the latest transaction date if present
        return candidates
            .OrderByDescending(x => x.PayloadSnapshot?.TransactionDate ?? x.TransactionDate ?? DateTime.MinValue)
            .FirstOrDefault();
    }

    /// <summary>
    /// Builds a dimension signature string for reversal-triggering attributes.
    ///
    /// Warehouse is included ONLY for Item journals.
    /// </summary>
    private static string BuildSignature(FscmJournalLine x, JournalType journalType)
    {
        var dept = x.Department?.Trim();
        var pl = x.ProductLine?.Trim();
        var lp = x.LineProperty?.Trim();

        var wh = journalType == JournalType.Item
            ? x.Warehouse?.Trim()
            : null;

        var cpid = x.CustomerProductId?.Trim();
        var cpdesc = x.CustomerProductDescription?.Trim();
        var tax = x.TaxabilityType?.Trim();

        var price = x.CalculatedUnitPrice.HasValue
            ? x.CalculatedUnitPrice.Value.ToString("0.########")
            : string.Empty;

        return $"D={dept}|PL={pl}|W={wh}|LP={lp}|CPID={cpid}|CPDESC={cpdesc}|TAX={tax}|P={price}";
    }
}

/// <summary>
/// FSCM journal line aggregation extensions.
/// </summary>
internal static class FscmJournalLineAggExtensions
{
    public static decimal SumSafeQty(this IEnumerable<FscmJournalLine> lines)
        => lines.Sum(x => x.Quantity);

    public static decimal? SumSafeExtAmount(this IEnumerable<FscmJournalLine> lines)
    {
        if (lines is null) return null;

        decimal sum = 0m;
        bool any = false;

        foreach (var ln in lines)
        {
            if (ln.ExtendedAmount.HasValue)
            {
                sum += ln.ExtendedAmount.Value;
                any = true;
            }
        }

        return any ? sum : (decimal?)null;
    }

    public static decimal? EffectiveUnitPriceOrNull(this IEnumerable<FscmJournalLine> lines)
    {
        if (lines is null) return null;

        var prices = lines
            .Select(x => x.CalculatedUnitPrice)
            .Where(p => p.HasValue)
            .Select(p => p!.Value)
            .Distinct()
            .ToList();

        if (prices.Count == 1)
            return prices[0];

        if (prices.Count > 1)
            return null;

        var qty = lines.SumSafeQty();
        if (qty == 0m)
            return null;

        var ext = lines.SumSafeExtAmount();
        if (ext is null)
            return null;

        return ext.Value / qty;
    }
}
