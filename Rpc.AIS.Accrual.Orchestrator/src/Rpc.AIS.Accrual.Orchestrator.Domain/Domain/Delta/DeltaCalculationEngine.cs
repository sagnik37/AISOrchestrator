// File: Rpc.AIS.Accrual.Orchestrator/src/Rpc.AIS.Accrual.Orchestrator.Core/Domain/Delta/DeltaCalculationEngine.cs
//
//
// NOTE (SRP refactor):
// - This class is now a thin façade delegating to DeltaMathEngine + helpers:
//     DeltaBucketBuilder (bucket/line construction)
//     DeltaGrouping      (signature/grouping helpers)
//     DeltaEdgeCaseRules (period + field-change reversal rules)
//     DeltaMathEngine    (overall delta workflow)

namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;

using System;
using System.Threading;
using System.Threading.Tasks;

public sealed class DeltaCalculationEngine
{
    private readonly JournalReversalPlanner _reversalPlanner;

    public DeltaCalculationEngine(JournalReversalPlanner reversalPlanner)
    {
        _reversalPlanner = reversalPlanner ?? throw new ArgumentNullException(nameof(reversalPlanner));
    }

    public Task<DeltaCalculationResult> CalculateAsync(
        FsaWorkOrderLineSnapshot fsa,
        FscmWorkOrderLineAggregation? fscmAgg,
        AccountingPeriodSnapshot period,
        DateTime today,
        CancellationToken ct,
        string? reasonPrefix = null)
        => DeltaMathEngine.CalculateAsync(_reversalPlanner, fsa, fscmAgg, period, today, ct, reasonPrefix);
}
