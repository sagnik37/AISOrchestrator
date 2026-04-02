using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Thin HTTP adapter for AdHocBatch_SingleJob. Delegates to JobOperationsHttpHandlerCommon.
/// </summary>
public sealed class AdHocBatchSingleJobFunction
{
    private readonly IAdHocSingleJobUseCase _useCase;

    public AdHocBatchSingleJobFunction(IAdHocSingleJobUseCase useCase)
        => _useCase = useCase ?? throw new System.ArgumentNullException(nameof(useCase));

    [Function("AdHocBatch_SingleJob")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "adhoc/batch/single")] HttpRequestData req,
        FunctionContext ctx)
    {
        return await _useCase.ExecuteAsync(req, ctx);
    }
}
