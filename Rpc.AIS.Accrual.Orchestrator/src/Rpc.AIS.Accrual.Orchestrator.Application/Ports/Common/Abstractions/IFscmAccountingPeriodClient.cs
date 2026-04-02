using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Defines i fscm accounting period client behavior.
/// </summary>
public interface IFscmAccountingPeriodClient
{
    Task<AccountingPeriodSnapshot> GetSnapshotAsync(RunContext context, CancellationToken ct);
}
