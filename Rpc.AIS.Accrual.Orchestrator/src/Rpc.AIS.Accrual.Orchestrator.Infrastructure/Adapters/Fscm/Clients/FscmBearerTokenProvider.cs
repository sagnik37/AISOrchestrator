using System.Collections.Concurrent;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Cached AAD bearer token provider for FSCM (client credentials).
/// Token cache is per-scope (per host), safe for multiple AOS instances.
/// Register as singleton.
/// </summary>
public sealed class FscmBearerTokenProvider : IFscmTokenProvider
{
    private readonly ClientSecretCredential _credential;

    // per-scope cache + lock
    private readonly ConcurrentDictionary<string, AccessToken> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public FscmBearerTokenProvider(IOptions<FscmOptions> options)
    {
        var o = options?.Value ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(o.TenantId)) throw new InvalidOperationException("Fscm:Auth:TenantId is missing.");
        if (string.IsNullOrWhiteSpace(o.ClientId)) throw new InvalidOperationException("Fscm:Auth:ClientId is missing.");
        if (string.IsNullOrWhiteSpace(o.ClientSecret)) throw new InvalidOperationException("Fscm:Auth:ClientSecret is missing.");

        _credential = new ClientSecretCredential(o.TenantId, o.ClientId, o.ClientSecret);
    }

    /// <summary>
    /// Executes get access token async.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(string scope, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("Scope must not be empty.", nameof(scope));

        // Fast path
        if (_cache.TryGetValue(scope, out var cached) &&
            !string.IsNullOrWhiteSpace(cached.Token) &&
            cached.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return cached.Token;
        }

        var gate = _locks.GetOrAdd(scope, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(ct);
        try
        {
            // Double-check after lock
            if (_cache.TryGetValue(scope, out cached) &&
                !string.IsNullOrWhiteSpace(cached.Token) &&
                cached.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return cached.Token;
            }

            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { scope }),
                ct);

            _cache[scope] = token;
            return token.Token;
        }
        finally
        {
            gate.Release();
        }
    }
}
