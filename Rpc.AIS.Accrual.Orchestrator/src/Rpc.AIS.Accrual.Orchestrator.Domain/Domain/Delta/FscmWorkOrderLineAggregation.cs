using System;
using System.Collections.Generic;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;

/// <summary>
/// Second-level aggregation bucket for FSCM journal history.
///
/// Keyed by reversal-triggering attributes (warehouse  excluded):
/// - rpc_department
/// - rpc_productline
/// - msdyn_lineproperty
/// - rpc_calculatedunitprice
///
/// This enables deterministic inspection / audit of FSCM history when the above attributes changed.
/// </summary>
public sealed record FscmDimensionBucket
(
    string? Department,
    string? ProductLine,
    string? Warehouse,
    string? LineProperty,
    string? CustomerProductId,
    string? CustomerProductDescription,
    string? TaxabilityType,
    decimal? CalculatedUnitPrice,
    decimal SumQuantity,
    decimal? SumExtendedAmount,
    IReadOnlyList<DateTime?> TransactionDates
);

/// <summary>
/// Reversal-safe FSCM history profile for a single effective attribute signature.
/// This allows AIS to reverse mixed Bill/NonBill (and similar) history without collapsing it
/// into a single representative line that would leave residual quantities behind.
/// </summary>
public sealed record FscmReversalProfile
(
    string? Department,
    string? ProductLine,
    string? Warehouse,
    string? LineProperty,
    string? CustomerProductId,
    string? CustomerProductDescription,
    string? TaxabilityType,
    decimal? CalculatedUnitPrice,
    decimal NetQuantity,
    decimal? NetExtendedAmount,
    IReadOnlyList<FscmDateBucket> DateBuckets,
    FscmReversalPayloadSnapshot? RepresentativeSnapshot = null
);

/// <summary>
/// Aggregated view of FSCM journals for a single WorkOrder line (grouped by WorkOrderLineId).
/// </summary>
public sealed record FscmWorkOrderLineAggregation
(
    Guid WorkOrderLineId,
    Guid WorkOrderId,
    JournalType JournalType,

    decimal TotalQuantity,
    decimal? TotalExtendedAmount,
    decimal? EffectiveUnitPrice,

    // <summary>
    // Distinct signatures of reversal-triggering attributes across the FSCM history.
    // Useful for troubleshooting and audit telemetry.
    //
    //  Warehouse is  excluded from signatures .
    // </summary>
    IReadOnlyList<string> DimensionSignatures,

    // <summary>
    // Buckets grouped by TransactionDate.Date (null date bucket supported).
    // Used by reversal planner to split closed vs open.
    // </summary>
    IReadOnlyList<FscmDateBucket> DateBuckets,

    // <summary>
    // Second-level grouping by (Dept, ProductLine, LineProperty, UnitPrice) with sums.
    // Populated by <see cref="FscmJournalAggregator"/>.
    // </summary>
    IReadOnlyList<FscmDimensionBucket> DimensionBuckets,

    // Representative FSCM line snapshot for reversal payload mapping (optional)
    FscmReversalPayloadSnapshot? RepresentativeSnapshot = null,

    // Signature-level profiles used for precise reversal generation.
    IReadOnlyList<FscmReversalProfile>? ReversalProfiles = null
)
{
    /// <summary>
    /// Backwards-compatible constructor for call sites that haven't been updated yet.
    /// </summary>
    public FscmWorkOrderLineAggregation(
        Guid WorkOrderLineId,
        Guid WorkOrderId,
        JournalType JournalType,
        decimal TotalQuantity,
        decimal? TotalExtendedAmount,
        decimal? EffectiveUnitPrice,
        IReadOnlyList<string> DimensionSignatures,
        IReadOnlyList<FscmDateBucket> DateBuckets)
        : this(
            WorkOrderLineId,
            WorkOrderId,
            JournalType,
            TotalQuantity,
            TotalExtendedAmount,
            EffectiveUnitPrice,
            DimensionSignatures,
            DateBuckets,
            Array.Empty<FscmDimensionBucket>(),
            null,
            Array.Empty<FscmReversalProfile>())
    {
    }
}
