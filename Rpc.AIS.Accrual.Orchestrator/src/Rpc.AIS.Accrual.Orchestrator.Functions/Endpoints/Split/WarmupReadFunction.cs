using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

public sealed class WarmupReadFunction
{
    private readonly IWarmupReadUseCase _useCase;

    public WarmupReadFunction(IWarmupReadUseCase useCase) => _useCase = useCase;

    [Function("Warmup_Read")]
    public Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "warmup/read")] HttpRequestData req,
        FunctionContext ctx)
        => _useCase.ExecuteAsync(req, ctx);
}
