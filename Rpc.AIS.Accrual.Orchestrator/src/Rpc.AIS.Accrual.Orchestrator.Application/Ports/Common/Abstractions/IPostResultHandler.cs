using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Extensibility point invoked after a posting attempt.
/// Handlers can implement additional follow-up actions (notifications, compensations, retries, etc.)
/// without modifying the posting client, maintaining Open/Closed Principle.
/// </summary>
public interface IPostResultHandler
{
    /// <summary>
    /// Returns true if this handler should run for the given post result.
    /// </summary>
    bool CanHandle(PostResult result);

    /// <summary>
    /// Performs the handler's post-processing action.
    /// </summary>
    Task HandleAsync(RunContext context, PostResult result, CancellationToken ct);
}
