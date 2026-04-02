using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;

/// <summary>
/// Builds FSCM HTTP requests (posting + custom validation), keeping headers and content setup consistent.
/// </summary>
public interface IFscmPostRequestFactory
{
    HttpRequestMessage CreateJsonPost(RunContext ctx, string url, string payloadJson);
}

public sealed class FscmPostRequestFactory : IFscmPostRequestFactory
{
    public HttpRequestMessage CreateJsonPost(RunContext ctx, string url, string payloadJson)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("URL is empty.", nameof(url));
        if (payloadJson is null) throw new ArgumentNullException(nameof(payloadJson));

        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.TryAddWithoutValidation("x-run-id", ctx.RunId);
        req.Headers.TryAddWithoutValidation("x-correlation-id", ctx.CorrelationId);
        req.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
        return req;
    }
}
