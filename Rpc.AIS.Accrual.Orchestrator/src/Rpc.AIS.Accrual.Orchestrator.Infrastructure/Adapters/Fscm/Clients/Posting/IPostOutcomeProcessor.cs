using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;

/// <summary>
/// Executes a post and converts the HTTP response into <see cref="PostResult"/>,
/// including running <see cref="Rpc.AIS.Accrual.Orchestrator.Core.Abstractions.IPostResultHandler"/> handlers.
/// </summary>
public interface IPostOutcomeProcessor
{
    Task<PostResult> PostAndProcessAsync(
        RunContext ctx,
        PreparedWoPosting prepared,
        CancellationToken ct);
}
