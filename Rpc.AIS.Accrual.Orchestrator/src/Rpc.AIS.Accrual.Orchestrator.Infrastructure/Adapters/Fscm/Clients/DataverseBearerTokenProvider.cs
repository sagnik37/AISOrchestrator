using System.Collections.Concurrent;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Provides dataverse bearer token provider behavior.
/// </summary>
public sealed class DataverseBearerTokenProvider : IDataverseTokenProvider
{
    private readonly ClientSecretCredential _credential;
    private readonly ConcurrentDictionary<string, AccessToken> _cache = new();

    public DataverseBearerTokenProvider(IOptions<FsOptions> opt)
    {
        var o = opt?.Value ?? throw new ArgumentNullException(nameof(opt));
        _credential = new ClientSecretCredential(o.TenantId, o.ClientId, o.ClientSecret);
    }

    /// <summary>
    /// Executes get access token async.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(string resource, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(resource))
            throw new ArgumentException("Resource must not be empty.", nameof(resource));

        var scope = resource.TrimEnd('/') + "/.default";

        if (_cache.TryGetValue(scope, out var cached) && cached.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(2))
            return cached.Token;

        var token = await _credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), ct);
        _cache[scope] = token;
        return token.Token;
    }
}
