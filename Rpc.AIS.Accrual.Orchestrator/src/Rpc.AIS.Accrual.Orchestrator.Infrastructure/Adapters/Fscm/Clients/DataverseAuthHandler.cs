using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Provides dataverse auth handler behavior.
/// </summary>
public sealed class DataverseAuthHandler : DelegatingHandler
{
    private readonly IDataverseTokenProvider _tokens;
    private readonly IOptions<FsOptions> _ingestion;
    private readonly ILogger<DataverseAuthHandler> _log;

    public DataverseAuthHandler(
        IDataverseTokenProvider tokens,
        IOptions<FsOptions> ingestion,
        ILogger<DataverseAuthHandler> log)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _ingestion = ingestion ?? throw new ArgumentNullException(nameof(ingestion));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var baseUrl = _ingestion.Value.DataverseApiBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("FsaIngestion:DataverseApiBaseUrl (via FsOptions) is missing.");

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            throw new InvalidOperationException($"FsaIngestion:DataverseApiBaseUrl (via FsOptions) is not a valid absolute URL: '{baseUrl}'.");

        // Token audience/resource for Dataverse should be the authority, not /api/data/...
        // Example: https://{org}.crm.dynamics.com
        var resource = baseUri.GetLeftPart(UriPartial.Authority);

        string token;
        try
        {
            token = await _tokens.GetAccessTokenAsync(resource, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DataverseAuthHandler: token acquisition failed. Resource={Resource}", resource);
            throw;
        }

        // Overwrite Authorization every time (safe for redirects/retries)
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // OData headers (idempotent)
        request.Headers.Remove("OData-MaxVersion");
        request.Headers.Remove("OData-Version");
        request.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
        request.Headers.TryAddWithoutValidation("OData-Version", "4.0");

        // Prefer header: useful for annotations; safe even if ignored
        request.Headers.Remove("Prefer");
        request.Headers.TryAddWithoutValidation("Prefer", "odata.include-annotations=\"*\"");

        // Avoid duplicating Accept header on each handler invocation
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Optional diagnostic (no secrets)
        _log.LogDebug(
            "DataverseAuthHandler: Auth applied. Method={Method} Uri={Uri} Resource={Resource}",
            request.Method.Method,
            request.RequestUri?.ToString() ?? "<null>",
            resource);

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
