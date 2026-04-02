using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Resilience;

public interface IResilientHttpExecutor
{
    Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        Func<HttpRequestMessage> requestFactory,
        RunContext ctx,
        string operationName,
        CancellationToken ct);
}
