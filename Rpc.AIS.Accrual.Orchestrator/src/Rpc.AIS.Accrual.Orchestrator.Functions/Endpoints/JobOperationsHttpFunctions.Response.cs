using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker.Http;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

public sealed partial class JobOperationsHttpHandlerCommon
{
    private static async Task<HttpResponseData> CreateDedicatedEndpointResponseAsync(HttpRequestData req)
    {
        var resp = req.CreateResponse(HttpStatusCode.BadRequest);
        resp.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await resp.WriteStringAsync(JsonSerializer.Serialize(new
        {
            message = "AdHocBatch_AllJobs is handled by the dedicated endpoint adapter (AdHocBatchAllJobsFunction) and IAdHocAllJobsUseCase."
        }));
        return resp;
    }
}
