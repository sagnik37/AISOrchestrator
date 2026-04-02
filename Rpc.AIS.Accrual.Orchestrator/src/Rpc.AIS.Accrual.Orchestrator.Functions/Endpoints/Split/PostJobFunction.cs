using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Thin HTTP adapter for PostJob. Delegates to JobOperationsHttpHandlerCommon.
/// </summary>
public sealed class PostJobFunction
{
    private readonly IPostJobUseCase _useCase;

    public PostJobFunction(IPostJobUseCase useCase)
        => _useCase = useCase ?? throw new System.ArgumentNullException(nameof(useCase));

    [Function("PostJob")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "job/post")] HttpRequestData req,
        FunctionContext ctx)
    {
        return await _useCase.ExecuteAsync(req, ctx);
    }
}
