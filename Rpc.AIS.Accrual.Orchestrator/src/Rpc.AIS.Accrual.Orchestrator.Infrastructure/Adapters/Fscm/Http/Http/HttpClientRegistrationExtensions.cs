using System;
using System.Net.Http;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Polly;

using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Http;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure;

internal static class HttpClientRegistrationExtensions
{
    internal enum HttpCategory
    {
        Dataverse,
        Fscm
    }

    internal static IHttpClientBuilder AddAisHttpClientWithPolicies<TClient, TImpl>(
        this IServiceCollection services,
        string clientName,
        HttpCategory category)
        where TClient : class
        where TImpl : class, TClient
    {
        return services.AddHttpClient<TClient, TImpl>()
            .SetHandlerLifetime(TimeSpan.FromMinutes(10))
            .AddPolicyHandler((sp, req) =>
            {
                var opts = sp.GetRequiredService<IOptions<HttpPolicyOptions>>().Value;
                var cat = category == HttpCategory.Dataverse ? opts.Dataverse : opts.Fscm;
                return HttpPolicies.BuildTimeoutPolicy(TimeSpan.FromSeconds(cat.TimeoutSeconds));
            })
            .AddPolicyHandler((sp, req) =>
            {
                // IMPORTANT: Never retry HTTP POST (non-idempotent). Retrying can create duplicate side effects.
                if (req?.Method == HttpMethod.Post)
                    return Policy.NoOpAsync<HttpResponseMessage>();

                var opts = sp.GetRequiredService<IOptions<HttpPolicyOptions>>().Value;
                var cat = category == HttpCategory.Dataverse ? opts.Dataverse : opts.Fscm;
                return HttpPolicies.BuildRetryPolicy(sp, clientName, cat);
            });
    }
}
