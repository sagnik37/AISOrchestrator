using System.Net.Http;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Factory used by typed <see cref="IPostingClient"/> to build a posting workflow
/// using the provided typed <see cref="HttpClient"/> instance.
/// </summary>
public interface IPostingWorkflowFactory
{
    IPostingClient Create(HttpClient httpClient);
}
