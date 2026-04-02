using System;
using System.Collections.Generic;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;

/// <summary>
/// Carries delta calculation result data.
/// </summary>
public sealed record DeltaCalculationResult(
    Guid WorkOrderId,
    Guid WorkOrderLineId,
    DeltaDecision Decision,
    IReadOnlyList<DeltaPlannedLine> Lines,
    string Reason
);

/// <summary>
/// Defines delta decision values.
/// </summary>
public enum DeltaDecision
{
    NoChange = 0,

    /// <summary>Quantity-only delta line.</summary>
    QuantityDelta = 1,

    /// <summary>Reverse all history, then add a positive line with updated attributes.</summary>
    ReverseAndRecreate = 2,

    /// <summary>Reverse all history only (inactive scenario).</summary>
    ReverseOnly = 3
}

/// <summary>
/// Normalized planned journal line produced by delta engine.
/// Later,  payload builder maps this into WOItemLines / WOExpLines / WOHourLines.
/// </summary>
public sealed record DeltaPlannedLine(
    DateTime TransactionDate,
    decimal Quantity,

    // These represent the "effective" attributes for the new/reversal line
    decimal? CalculatedUnitPrice,
    decimal? ExtendedAmount,

    string? LineProperty,
    string? Department,
    string? ProductLine,
    string? Warehouse,

    bool IsReversal,
    bool FromClosedPeriodSplit,
    string LineReason,
    FscmReversalPayloadSnapshot? ReversalPayloadSnapshotOverride = null
);
