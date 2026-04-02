using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Function-layer use case for job operation HTTP endpoints.
/// Keeps Function adapters thin and enables substitutable implementations.
/// </summary>
public interface IJobOperationsHttpUseCase
{
    Task<HttpResponseData> AdHocSingleJobSyncAsync(HttpRequestData req, FunctionContext ctx);

    Task<HttpResponseData> AdHocAllJobsAsync(HttpRequestData req, FunctionContext ctx);

    Task<HttpResponseData> PostJobSyncAsync(HttpRequestData req, FunctionContext ctx);

    Task<HttpResponseData> CancelJobSyncAsync(HttpRequestData req, FunctionContext ctx);

    Task<HttpResponseData> CustomerChangeSyncAsync(HttpRequestData req, FunctionContext ctx);
}
