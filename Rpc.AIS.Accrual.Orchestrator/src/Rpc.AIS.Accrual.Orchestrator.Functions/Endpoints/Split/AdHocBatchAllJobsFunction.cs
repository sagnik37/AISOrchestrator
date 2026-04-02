using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Thin HTTP adapter for AdHocBatch_AllJobs. Delegates to JobOperationsHttpHandlerCommon.
/// </summary>
public sealed class AdHocBatchAllJobsFunction
{
    private readonly IAdHocAllJobsUseCase _useCase;

    public AdHocBatchAllJobsFunction(IAdHocAllJobsUseCase useCase)
        => _useCase = useCase ?? throw new System.ArgumentNullException(nameof(useCase));

    [Function("AdHocBatch_AllJobs")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "adhoc/batch/all")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        FunctionContext ctx)
    {
        return await _useCase.ExecuteAsync(req, client, ctx);
    }
}
