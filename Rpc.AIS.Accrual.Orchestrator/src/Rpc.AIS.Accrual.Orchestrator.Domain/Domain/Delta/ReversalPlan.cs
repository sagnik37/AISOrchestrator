using System;
using System.Collections.Generic;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;

/// <summary>
/// Result produced by JournalReversalPlanner.
/// </summary>
public sealed record ReversalPlan(
    Guid WorkOrderLineId,
    decimal TotalQuantityToReverse,
    IReadOnlyList<PlannedReversal> Reversals
);

/// <summary>
/// Carries planned reversal data.
/// </summary>
public sealed record PlannedReversal(
    DateTime TransactionDate,
    decimal Quantity,
    bool FromClosedPeriod,
    string Reason
);
