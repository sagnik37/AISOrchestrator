using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

public sealed class AdHocBatchAllStatusFunction
{
    private readonly IAdHocAllStatusUseCase _useCase;

    public AdHocBatchAllStatusFunction(IAdHocAllStatusUseCase useCase)
        => _useCase = useCase ?? throw new System.ArgumentNullException(nameof(useCase));

    [Function("AdHocBatch_AllJobs_Status")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "adhoc/batch/status")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        FunctionContext ctx)
    {
        return await _useCase.ExecuteAsync(req, client, ctx);
    }
}
