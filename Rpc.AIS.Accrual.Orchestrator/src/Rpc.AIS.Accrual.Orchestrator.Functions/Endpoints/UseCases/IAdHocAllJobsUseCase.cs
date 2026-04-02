using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Use case for AdHoc Batch - All Jobs (durable schedule).
/// </summary>
public interface IAdHocAllJobsUseCase
{
    Task<HttpResponseData> ExecuteAsync(HttpRequestData req, DurableTaskClient client, FunctionContext ctx);
}
