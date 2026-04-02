using System;
using System.Collections.Generic;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;

/// <summary>
/// Represents FSCM journal lines for a WorkOrder line grouped by a specific transaction date (date-only).
/// </summary>
public sealed record FscmDateBucket
(
    DateTime? TransactionDate,
    decimal SumQuantity,
    decimal? SumExtendedAmount,
    decimal? EffectiveUnitPrice,

    IReadOnlyList<FscmJournalLine> Lines
);
