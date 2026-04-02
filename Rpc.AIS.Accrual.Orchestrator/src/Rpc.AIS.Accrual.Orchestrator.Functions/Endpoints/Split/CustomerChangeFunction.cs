using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Thin HTTP adapter for CustomerChange. Delegates to JobOperationsHttpHandlerCommon.
/// </summary>
public sealed class CustomerChangeFunction
{
    private readonly ICustomerChangeUseCase _useCase;

    public CustomerChangeFunction(ICustomerChangeUseCase useCase)
        => _useCase = useCase ?? throw new System.ArgumentNullException(nameof(useCase));

    [Function("CustomerChange")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "job/customer-change")] HttpRequestData req,
        FunctionContext ctx)
    {
        return await _useCase.ExecuteAsync(req, ctx);
    }
}
