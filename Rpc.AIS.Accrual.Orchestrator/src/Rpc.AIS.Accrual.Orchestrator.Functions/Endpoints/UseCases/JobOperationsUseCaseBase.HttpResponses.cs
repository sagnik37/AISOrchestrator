using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker.Http;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

public abstract partial class JobOperationsUseCaseBase
{
    protected static Task<HttpResponseData> OkAsync(HttpRequestData req, string correlationId, string runId, object payload)
        => CreateJsonResponseAsync(req, HttpStatusCode.OK, correlationId, runId, payload);

    protected static Task<HttpResponseData> AcceptedAsync(HttpRequestData req, string correlationId, string runId, object payload)
        => CreateJsonResponseAsync(req, HttpStatusCode.Accepted , correlationId, runId, payload);

    protected static Task<HttpResponseData> NotFoundAsync(HttpRequestData req, string correlationId, string runId, object payload)
        => CreateJsonResponseAsync(req, HttpStatusCode.NotFound, correlationId, runId, payload);

    protected static Task<HttpResponseData> BadRequestAsync(HttpRequestData req, string correlationId, string runId, string message)
        => CreateJsonResponseAsync(req, HttpStatusCode.BadRequest, correlationId, runId, new { runId, correlationId, message });

    protected static Task<HttpResponseData> BadGatewayAsync(HttpRequestData req, string correlationId, string runId, string message)
        => CreateJsonResponseAsync(req, HttpStatusCode.BadGateway, correlationId, runId, new { runId, correlationId, message });

    protected static Task<HttpResponseData> ServerErrorAsync(HttpRequestData req, string correlationId, string runId, string message)
        => CreateJsonResponseAsync(req, HttpStatusCode.InternalServerError, correlationId, runId, new { runId, correlationId, message });

    private static async Task<HttpResponseData> CreateJsonResponseAsync(HttpRequestData req, HttpStatusCode statusCode, string correlationId, string runId, object payload)
    {
        var resp = req.CreateResponse(statusCode);
        resp.Headers.Add("Content-Type", "application/json; charset=utf-8");
        resp.Headers.Add("x-correlation-id", correlationId);
        resp.Headers.Add("x-run-id", runId);
        await resp.WriteStringAsync(JsonSerializer.Serialize(payload));
        return resp;
    }
}
