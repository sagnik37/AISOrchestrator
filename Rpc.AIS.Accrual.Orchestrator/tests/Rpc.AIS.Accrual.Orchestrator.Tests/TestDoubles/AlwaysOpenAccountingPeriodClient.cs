using System;
using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;

namespace Rpc.AIS.Accrual.Orchestrator.Tests.TestDoubles;

public sealed class AlwaysOpenAccountingPeriodClient : IFscmAccountingPeriodClient
{
    public Task<AccountingPeriodSnapshot> GetSnapshotAsync(RunContext context, CancellationToken ct)
    {
        var now = DateTime.UtcNow.Date;
        var snap = new AccountingPeriodSnapshot(
            CurrentOpenPeriodStartDate: now,
            ClosedReversalDateStrategy: "None",
            SnapshotMinDate: now.AddYears(-1),
            SnapshotMaxDate: now.AddYears(1),
            IsDateInClosedPeriodAsync: (d, _) => ValueTask.FromResult(false),
            ResolveTransactionDateUtcAsync: (d, _) => ValueTask.FromResult(d));

        return Task.FromResult(snap);
    }
}
