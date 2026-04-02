using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

public interface IWarmupReadUseCase
{
    Task<HttpResponseData> ExecuteAsync(HttpRequestData req, FunctionContext ctx);
}
