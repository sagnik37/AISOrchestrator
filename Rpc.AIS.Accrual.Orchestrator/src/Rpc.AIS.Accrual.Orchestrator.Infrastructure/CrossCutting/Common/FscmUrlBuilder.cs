using System;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Utilities;

/// <summary>
/// Centralized FSCM URL construction and base-url resolution.
/// Extracted to keep HTTP clients focused on orchestration (SRP) and make URL semantics reusable.
/// </summary>
public static class FscmUrlBuilder
{
    /// <summary>
    /// Preferred: FscmOptions.FscmBaseUrl (single unified host).
    /// Fallback: legacy per-endpoint base URLs.
    /// </summary>
    public static string ResolveFscmBaseUrl(FscmOptions endpoints, string? legacyBaseUrl, string legacyName)
    {
        if (endpoints is null) throw new ArgumentNullException(nameof(endpoints));

        if (!string.IsNullOrWhiteSpace(endpoints.BaseUrl))
            return endpoints.BaseUrl.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(legacyBaseUrl))
            return legacyBaseUrl.TrimEnd('/');

        throw new InvalidOperationException(
            $"FSCM base URL is not configured. Set 'Fscm:BaseUrl' (preferred) or legacy override under 'Endpoints:{legacyName}'.");
    }

    /// <summary>
    /// Executes build url.
    /// </summary>
    public static string BuildUrl(string baseUrl, string path)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("FSCM base URL is not configured.");
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("FSCM path is not configured.");

        var baseFixed = baseUrl.TrimEnd('/');
        var pathFixed = path.StartsWith('/') ? path : "/" + path;

        return baseFixed + pathFixed;
    }
}
