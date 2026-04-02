using System.Net;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Calls the FSCM custom endpoint for SubProject creation.
/// Sends payload using the required FSCM contract envelope: { "_request": { ... } }.
/// Adds AAD bearer token using client-credentials.
///
/// Prod policy:
/// - 401/403 => fail-fast
/// - 429/5xx => transient => throw (durable retry)
/// - other 4xx => non-transient => return error (do not throw)
/// - 2xx with FSCM business Status=Error => return exact FSCM message
/// </summary>
public sealed class FscmSubProjectHttpClient : IFscmSubProjectClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    private readonly HttpClient _http;
    private readonly IFscmTokenProvider _tokenProvider;
    private readonly FscmOptions _endpoints;
    private readonly ILogger<FscmSubProjectHttpClient> _log;

    public FscmSubProjectHttpClient(
        HttpClient http,
        IFscmTokenProvider tokenProvider,
        IOptions<FscmOptions> endpoints,
        ILogger<FscmSubProjectHttpClient> log)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _endpoints = endpoints?.Value ?? throw new ArgumentNullException(nameof(endpoints));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<SubProjectCreateResult> CreateSubProjectAsync(
        RunContext context,
        SubProjectCreateRequest request,
        CancellationToken ct)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (request is null) throw new ArgumentNullException(nameof(request));

        var baseUrl = ResolveBaseUrl(_endpoints.SubProjectBaseUrlOverride, legacyName: "SubProjectBaseUrlOverride");
        if (string.IsNullOrWhiteSpace(_endpoints.SubProjectPath))
            throw new InvalidOperationException("Endpoints:SubProjectPath is missing.");

        var baseUri = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var relativePath = _endpoints.SubProjectPath.TrimStart('/');
        var requestUri = new Uri(baseUri, relativePath);

        // Scope resolution:
        // 1) ScopesByHost override
        // 2) DefaultScope (if provided)
        // 3) Derive from request host
        var scope = ResolveScopeForHost(requestUri.Host);

        var token = await _tokenProvider.GetAccessTokenAsync(scope, ct).ConfigureAwait(false);

        var reqDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        AddRequiredString(reqDict, "DataAreaId", request.DataAreaId, nameof(request.DataAreaId));
        AddRequiredString(reqDict, "ParentProjectId", request.ParentProjectId, nameof(request.ParentProjectId));
        AddRequiredString(reqDict, "WorkOrderId", request.ProjectName, nameof(request.ProjectName));

        // Field Service / Customer Change contract fields (optional)
        AddOptionalString(reqDict, "WorkOrderGUID", request.WorkOrderGuid);

        if (request.IsFsaProject.HasValue)
            reqDict["IsFSAProject"] = request.IsFsaProject.Value;

        if (request.ProjectStatus.HasValue)
            reqDict["ProjectStatus"] = request.ProjectStatus.Value;

        //if (!string.IsNullOrWhiteSpace(request.ProjectName))
        //    reqDict["WorkOrderId"] = request.ProjectName;

        AddOptionalString(reqDict, "CustomerReference", request.CustomerReference);
        AddOptionalString(reqDict, "InvoiceNotes", request.InvoiceNotes);
        AddOptionalString(reqDict, "AddressName", request.AddressName);
        AddOptionalString(reqDict, "Street", request.Street);
        AddOptionalString(reqDict, "City", request.City);
        AddOptionalString(reqDict, "State", request.State);
        AddOptionalString(reqDict, "County", request.County);
        AddOptionalString(reqDict, "CountryRegionId", request.CountryRegionId);
        AddOptionalString(reqDict, "WellName", request.WellName);
        AddOptionalString(reqDict, "WellNumber", request.WellNumber);
        AddOptionalString(reqDict, "WellLocale", request.WellLocale);
        AddOptionalString(reqDict, "ActualStartDate", request.ActualStartDate);
        AddOptionalString(reqDict, "ActualEndDate", request.ActualEndDate);

        var bodyObj = new Dictionary<string, object?>
        {
            ["_request"] = reqDict
        };

        var json = JsonSerializer.Serialize(bodyObj, JsonOptions);
        var payloadBytes = Encoding.UTF8.GetByteCount(json);

        using var msg = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        msg.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(context.RunId))
            msg.Headers.TryAddWithoutValidation("x-run-id", context.RunId);

        if (!string.IsNullOrWhiteSpace(context.CorrelationId))
            msg.Headers.TryAddWithoutValidation("x-correlation-id", context.CorrelationId);

        _log.LogInformation(
            "Calling FSCM SubProject endpoint. Url={Url} RunId={RunId} CorrelationId={CorrelationId} PayloadBytes={PayloadBytes} Payload={Payload}",
            requestUri,
            context.RunId,
            context.CorrelationId,
            payloadBytes,
            json);

        var sw = Stopwatch.StartNew();

        using var resp = await _http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        var respBody = await SafeReadBodyAsync(resp, ct).ConfigureAwait(false);

        sw.Stop();

        _log.LogInformation(
            "FSCM SubProject completed. StatusCode={StatusCode} Url={Url} RunId={RunId} CorrelationId={CorrelationId} ElapsedMs={ElapsedMs} ResponseBytes={ResponseBytes} ResponseBody={ResponseBody}",
            (int)resp.StatusCode,
            requestUri,
            context.RunId,
            context.CorrelationId,
            sw.ElapsedMilliseconds,
            Encoding.UTF8.GetByteCount(respBody ?? string.Empty),
            Truncate(respBody, 4000));

        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new UnauthorizedAccessException(
                $"FSCM SubProject call unauthorized/forbidden. HTTP {(int)resp.StatusCode}. Body: {Truncate(respBody, 4000)}");
        }

        if ((int)resp.StatusCode >= 400 && (int)resp.StatusCode <= 499)
        {
            _log.LogWarning(
                "FSCM SubProject failed (non-transient). StatusCode={StatusCode} RunId={RunId} CorrelationId={CorrelationId} Body={Body}",
                (int)resp.StatusCode,
                context.RunId,
                context.CorrelationId,
                Truncate(respBody, 4000));

            return new SubProjectCreateResult(
                IsSuccess: false,
                parmSubProjectId: null,
                Message: "FSCM subproject call failed (client error).",
                Errors: new[]
                {
                    new SubProjectError("FSCM_HTTP_4XX", $"HTTP {(int)resp.StatusCode}: {respBody}")
                });
        }

        if (resp.StatusCode == (HttpStatusCode)429 || (int)resp.StatusCode >= 500)
        {
            throw new HttpRequestException(
                $"FSCM SubProject call transient failure. HTTP {(int)resp.StatusCode}. Body: {Truncate(respBody, 4000)}",
                null,
                resp.StatusCode);
        }

        // HTTP success does not necessarily mean business success.
        var fscmStatus = TryExtractString(respBody, "Status");
        var fscmMessage = TryExtractString(respBody, "Message");
        var subProjectId = TryExtractSubProjectId(respBody ?? string.Empty);

        if (string.Equals(fscmStatus, "Error", StringComparison.OrdinalIgnoreCase))
        {
            var exactMessage = string.IsNullOrWhiteSpace(fscmMessage)
                ? "FSCM returned business error during subproject creation."
                : fscmMessage;

            _log.LogWarning(
                "FSCM SubProject business failure. FSCMStatus={FSCMStatus} FSCMMessage={FSCMMessage} RunId={RunId} CorrelationId={CorrelationId}",
                fscmStatus,
                exactMessage,
                context.RunId,
                context.CorrelationId);

            return new SubProjectCreateResult(
                IsSuccess: false,
                parmSubProjectId: null,
                Message: exactMessage,
                Errors: new[]
                {
                    new SubProjectError("FSCM_BUSINESS_ERROR", exactMessage)
                });
        }

        _log.LogInformation(
            "FSCM SubProject succeeded. StatusCode={StatusCode} FSCMStatus={FSCMStatus} SubProjectId={SubProjectId} RunId={RunId} CorrelationId={CorrelationId}",
            (int)resp.StatusCode,
            fscmStatus ?? "<null>",
            subProjectId ?? "<null>",
            context.RunId,
            context.CorrelationId);

        return new SubProjectCreateResult(
            IsSuccess: true,
            parmSubProjectId: subProjectId,
            Message: string.IsNullOrWhiteSpace(fscmMessage) ? "SubProject created successfully." : fscmMessage,
            Errors: Array.Empty<SubProjectError>());
    }

    private string ResolveBaseUrl(string? legacyBaseUrl, string legacyName)
    {
        if (!string.IsNullOrWhiteSpace(_endpoints.BaseUrl))
            return _endpoints.BaseUrl;

        if (!string.IsNullOrWhiteSpace(legacyBaseUrl))
            return legacyBaseUrl.TrimEnd('/');

        throw new InvalidOperationException(
            $"FSCM base URL is not configured. Set 'Fscm:BaseUrl' (preferred) or legacy 'Endpoints:{legacyName}'.");
    }

    private string ResolveScopeForHost(string host)
    {
        if (!string.IsNullOrWhiteSpace(host) &&
            _endpoints.ScopesByHost is not null &&
            _endpoints.ScopesByHost.TryGetValue(host, out var mapped) &&
            !string.IsNullOrWhiteSpace(mapped))
        {
            return mapped;
        }

        if (!string.IsNullOrWhiteSpace(_endpoints.DefaultScope))
            return _endpoints.DefaultScope;

        return $"https://{host}/.default";
    }

    private static async Task<string?> SafeReadBodyAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            return resp.Content is null ? string.Empty : await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? TryExtractSubProjectId(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            return TryGetString(root, "parmSubProjectId")
                ?? TryGetString(root, "subProjectId")
                ?? TryGetString(root, "SubProjectId")
                ?? TryGetNestedString(root, "result", "parmSubProjectId")
                ?? TryGetNestedString(root, "response", "parmSubProjectId");
        }
        catch
        {
            return null;
        }
    }

    private static string? TryExtractString(string? body, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            return TryGetString(doc.RootElement, propertyName);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.String)
                return prop.GetString();

            if (prop.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                return prop.ToString();
        }

        return null;
    }

    private static string? TryGetNestedString(JsonElement element, string objName, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        if (!element.TryGetProperty(objName, out var nested) || nested.ValueKind != JsonValueKind.Object)
            return null;

        return TryGetString(nested, propertyName);
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Length <= max ? s : (s.Substring(0, max) + " ...");
    }

    private static void AddOptionalString(IDictionary<string, object?> dict, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            dict[key] = value;
    }

    private static void AddRequiredString(IDictionary<string, object?> dict, string key, string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Required field '{key}' is missing/empty.", paramName);

        dict[key] = value;
    }
}
