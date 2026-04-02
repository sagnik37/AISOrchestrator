using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Thin HTTP adapter for CancelJob. Delegates to JobOperationsHttpHandlerCommon.
/// </summary>
public sealed class CancelJobFunction
{
    private readonly ICancelJobUseCase _useCase;

    public CancelJobFunction(ICancelJobUseCase useCase)
        => _useCase = useCase ?? throw new System.ArgumentNullException(nameof(useCase));

    [Function("CancelJob")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "job/cancel")] HttpRequestData req,
        FunctionContext ctx)
    {
        return await _useCase.ExecuteAsync(req, ctx);
    }
}
