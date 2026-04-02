using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Adds FSCM AAD bearer token to outbound HTTP requests.
/// Scope is resolved per-request from host using FscmOptions.
/// Attach ONLY to HttpClients that call FSCM.
/// </summary>
public sealed class FscmAuthHandler : DelegatingHandler
{
    private readonly IFscmTokenProvider _tokenProvider;
    private readonly FscmOptions _auth;
    private readonly ILogger<FscmAuthHandler> _logger;

    public FscmAuthHandler(
        IFscmTokenProvider tokenProvider,
        IOptions<FscmOptions> authOptions,
        ILogger<FscmAuthHandler> logger)
    {
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _auth = (authOptions?.Value) ?? throw new ArgumentNullException(nameof(authOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes send async.
    /// </summary>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        // Do not overwrite if already set (helps tests/mocks).
        if (request.Headers.Authorization is null)
        {
            var scope = ResolveScope(request.RequestUri);
            var token = await _tokenProvider.GetAccessTokenAsync(scope, ct);

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Safe log (never log token)
            _logger.LogDebug("Attached FSCM bearer token. Host={Host} Scope={Scope} Method={Method} Url={Url}",
                request.RequestUri?.Host, scope, request.Method, request.RequestUri);
        }

        return await base.SendAsync(request, ct);
    }

    /// <summary>
    /// Executes resolve scope.
    /// </summary>
    private string ResolveScope(Uri? uri)
    {
        if (uri is null || string.IsNullOrWhiteSpace(uri.Host))
        {
            if (!string.IsNullOrWhiteSpace(_auth.DefaultScope))
                return _auth.DefaultScope;

            throw new InvalidOperationException("Cannot resolve FSCM scope: request URI/host is missing and DefaultScope is empty.");
        }

        var host = uri.Host;

        // 1) Exact host override
        if (_auth.ScopesByHost is not null && _auth.ScopesByHost.TryGetValue(host, out var overrideScope) &&
            !string.IsNullOrWhiteSpace(overrideScope))
        {
            return overrideScope;
        }

        // 2) DefaultScope
        if (!string.IsNullOrWhiteSpace(_auth.DefaultScope))
            return _auth.DefaultScope;

        // 3) Derived from request host
        return $"https://{host}/.default";
    }
}
