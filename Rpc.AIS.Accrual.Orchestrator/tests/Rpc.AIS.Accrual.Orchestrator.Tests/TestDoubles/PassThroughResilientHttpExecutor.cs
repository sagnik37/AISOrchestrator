using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Resilience;

namespace Rpc.AIS.Accrual.Orchestrator.Tests.TestDoubles;

public sealed class PassThroughResilientHttpExecutor : IResilientHttpExecutor
{
    public Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        Func<HttpRequestMessage> requestFactory,
        RunContext ctx,
        string operationName,
        CancellationToken ct)
    {
        if (http is null) throw new ArgumentNullException(nameof(http));
        if (requestFactory is null) throw new ArgumentNullException(nameof(requestFactory));

        var req = requestFactory();
        return http.SendAsync(req, ct);
    }
}
