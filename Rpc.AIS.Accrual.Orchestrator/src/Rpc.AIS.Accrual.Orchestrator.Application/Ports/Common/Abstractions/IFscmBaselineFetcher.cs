using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

public interface IFscmBaselineFetcher
{
    Task<IReadOnlyList<FscmBaselineRecord>> FetchBaselineAsync(CancellationToken ct);
}
