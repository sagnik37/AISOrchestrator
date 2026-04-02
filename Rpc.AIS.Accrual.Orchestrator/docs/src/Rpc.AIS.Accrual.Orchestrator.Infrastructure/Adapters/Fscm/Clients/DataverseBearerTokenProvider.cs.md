# DataverseBearerTokenProvider 🔑

## Overview

The **DataverseBearerTokenProvider** class centralizes Azure AD token acquisition for Microsoft Dataverse. It uses the `ClientSecretCredential` from Azure Identity to fetch bearer tokens scoped to a Dataverse authority URL. Tokens are cached per scope to reduce round-trips and automatically refreshed when nearing expiration.

This provider fits into the FSCM adapter layer of the Accrual Orchestrator Infrastructure. Consumers—such as an HTTP message handler—request tokens without needing credential details or cache logic, ensuring consistent authentication across Dataverse calls.

## Dependencies

- Azure.Core
- Azure.Identity
- Microsoft.Extensions.Options
- Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options (`FsOptions`)

These packages supply the credential type, token abstractions, and configuration binding.

## Constructor

```csharp
public DataverseBearerTokenProvider(IOptions<FsOptions> opt)
```

- **Parameters:**- `opt` (IOptions<FsOptions>): Holds configuration values `TenantId`, `ClientId`, and `ClientSecret`.
- **Behavior:**- Throws `ArgumentNullException` if `opt` is null.
- Binds credential fields and initializes a `ClientSecretCredential` instance.

## Caching Strategy

- A private `ConcurrentDictionary<string, AccessToken>` stores tokens keyed by scope.
- Before fetching a new token, the cache is checked:- If an entry exists and its `ExpiresOn` is more than 2 minutes in the future, the cached token is returned.
- This **per-scope cache** prevents unnecessary token requests and is thread-safe.

## Public Methods

### GetAccessTokenAsync

```csharp
Task<string> GetAccessTokenAsync(string resource, CancellationToken ct)
```

| Parameter | Type | Description |
| --- | --- | --- |
| resource | string | Base authority URL (e.g., `https://{org}.crm.dynamics.com`). |
| ct | CancellationToken | Token for request cancellation. |


| Returns | Description |
| --- | --- |
| `Task<string>` | Asynchronously returns a valid bearer token string. |


**Flow:**

1. **Validation**- Throws `ArgumentException` if `resource` is null/empty.
2. **Scope Construction**- Appends `"/.default"` to the trimmed `resource` URL.
3. **Cache Lookup**- If a non-expired token exists for the scope, it is returned immediately.
4. **Token Request**- Calls `_credential.GetTokenAsync(...)` to obtain a new `AccessToken`.
5. **Cache Update**- Stores the new token in `_cache` before returning its `Token` property.

```csharp
public async Task<string> GetAccessTokenAsync(string resource, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(resource))
        throw new ArgumentException("Resource must not be empty.", nameof(resource));

    var scope = resource.TrimEnd('/') + "/.default";

    if (_cache.TryGetValue(scope, out var cached)
        && cached.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(2))
    {
        return cached.Token;
    }

    var token = await _credential.GetTokenAsync(
        new TokenRequestContext(new[] { scope }), ct);

    _cache[scope] = token;
    return token.Token;
}
```

## Usage Example

Register as a singleton in your DI container:

```csharp
services.AddSingleton<IDataverseTokenProvider, DataverseBearerTokenProvider>();
```

Then inject `IDataverseTokenProvider` into an HTTP message handler to attach bearer tokens to outgoing requests.

## Key Points

- **Thread-safe caching** avoids redundant token fetches.
- **Automatic scope normalization** ensures the correct AAD audience.
- **Minimal external dependencies**, leveraging Azure Identity and simple configuration via `FsOptions`.

This provider abstracts all authentication concerns, offering a clean interface for any component needing Dataverse access tokens.