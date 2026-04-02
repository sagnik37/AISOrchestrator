using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.InvoiceAttributes;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// FSCM custom endpoints for invoice attributes:
/// - Definitions ("attribute table")
/// - Current values snapshot
/// - Update values (InvoiceAttributes: [{AttributeName, AttributeValue}])
///
/// This client now acts as the orchestration layer only. Request construction,
/// HTTP transport/audit logging, and response parsing are delegated to smaller
/// collaborators to keep the client aligned with SRP/DIP.
/// </summary>
public sealed class FscmInvoiceAttributesHttpClient : IFscmInvoiceAttributesClient
{
    private readonly HttpClient _http;
    private readonly FscmOptions _opt;
    private readonly ILogger<FscmInvoiceAttributesHttpClient> _log;
    private readonly IInvoiceAttributesPayloadBuilder _payloadBuilder;
    private readonly IInvoiceAttributesHttpTransport _transport;
    private readonly IInvoiceAttributesResponseParser _responseParser;

    public FscmInvoiceAttributesHttpClient(
        HttpClient http,
        IOptions<FscmOptions> options,
        IInvoiceAttributesPayloadBuilder payloadBuilder,
        IInvoiceAttributesHttpTransport transport,
        IInvoiceAttributesResponseParser responseParser,
        ILogger<FscmInvoiceAttributesHttpClient> log)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _opt = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _payloadBuilder = payloadBuilder ?? throw new ArgumentNullException(nameof(payloadBuilder));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _responseParser = responseParser ?? throw new ArgumentNullException(nameof(responseParser));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<IReadOnlyList<InvoiceAttributeDefinition>> GetDefinitionsAsync(
        RunContext ctx,
        string company,
        string subProjectId,
        CancellationToken ct)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        if (string.IsNullOrWhiteSpace(company)) throw new ArgumentException("Company is required.", nameof(company));
        if (string.IsNullOrWhiteSpace(subProjectId)) throw new ArgumentException("SubProjectId is required.", nameof(subProjectId));

        if (string.IsNullOrWhiteSpace(_opt.InvoiceAttributeDefinitionsPath))
        {
            _log.LogWarning(
                "FSCM invoice attribute definitions endpoint is not configured. Skipping. RunId={RunId} CorrelationId={CorrelationId}",
                ctx.RunId, ctx.CorrelationId);
            return Array.Empty<InvoiceAttributeDefinition>();
        }

        var url = _payloadBuilder.BuildUrl(_opt.BaseUrl, _opt.InvoiceAttributeDefinitionsPath);
        var json = _payloadBuilder.BuildDefinitionsPayload(company, subProjectId);

        var response = await _transport.PostJsonAsync(
            _http,
            ctx,
            url,
            json,
            operation: "FSCM_INV_ATTR_DEFS",
            ct).ConfigureAwait(false);

        if ((int)response.StatusCode >= 400)
        {
            _log.LogWarning("FSCM attribute definitions failed. Status={Status} Body={Body}", (int)response.StatusCode, LogText.TrimForLog(response.Body));
            return Array.Empty<InvoiceAttributeDefinition>();
        }

        var defs = _responseParser.ParseDefinitions(response.Body);
        _log.LogInformation("FSCM attribute definitions parsed. Count={Count} ElapsedMs={ElapsedMs}", defs.Count, response.ElapsedMs);
        return defs;
    }

    public async Task<IReadOnlyList<InvoiceAttributePair>> GetCurrentValuesAsync(
        RunContext ctx,
        string company,
        string subProjectId,
        IReadOnlyList<string> attributeNames,
        CancellationToken ct)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        if (string.IsNullOrWhiteSpace(company)) throw new ArgumentException("Company is required.", nameof(company));
        if (string.IsNullOrWhiteSpace(subProjectId)) throw new ArgumentException("SubProjectId is required.", nameof(subProjectId));

        if (string.IsNullOrWhiteSpace(_opt.InvoiceAttributeValuesPath))
        {
            _log.LogWarning(
                "FSCM invoice attribute values endpoint is not configured. Skipping. RunId={RunId} CorrelationId={CorrelationId}",
                ctx.RunId, ctx.CorrelationId);
            return Array.Empty<InvoiceAttributePair>();
        }

        var url = _payloadBuilder.BuildUrl(_opt.BaseUrl, _opt.InvoiceAttributeValuesPath);
        var json = _payloadBuilder.BuildCurrentValuesPayload(company, subProjectId, attributeNames ?? Array.Empty<string>());

        var response = await _transport.PostJsonAsync(
            _http,
            ctx,
            url,
            json,
            operation: "FSCM_INV_ATTR_VALUES",
            ct).ConfigureAwait(false);

        if ((int)response.StatusCode >= 400)
        {
            _log.LogWarning("FSCM attribute values failed. Status={Status} Body={Body}", (int)response.StatusCode, LogText.TrimForLog(response.Body));
            return Array.Empty<InvoiceAttributePair>();
        }

        var values = _responseParser.ParseCurrentValues(response.Body);
        _log.LogInformation("FSCM attribute values parsed. Count={Count} ElapsedMs={ElapsedMs}", values.Count, response.ElapsedMs);
        return values;
    }

    public async Task<FscmInvoiceAttributesUpdateResult> UpdateAsync(
        RunContext ctx,
        string company,
        string subProjectId,
        Guid workOrderGuid,
        string workOrderId,
        string? countryRegionId,
        string? county,
        string? state,
        string? dimensionDisplayValue,
        string? fsaTaxabilityType,
        string? fsaWellAge,
        string? fsaWorkType,
        IReadOnlyDictionary<string, object?>? additionalHeaderFields,
        IReadOnlyList<InvoiceAttributePair> updates,
        CancellationToken ct)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        if (string.IsNullOrWhiteSpace(company)) throw new ArgumentException("Company is required.", nameof(company));
        if (string.IsNullOrWhiteSpace(subProjectId)) throw new ArgumentException("SubProjectId is required.", nameof(subProjectId));
        if (string.IsNullOrWhiteSpace(workOrderId)) throw new ArgumentException("WorkOrderId is required.", nameof(workOrderId));

        if (string.IsNullOrWhiteSpace(_opt.UpdateInvoiceAttributesPath))
        {
            _log.LogWarning(
                "FSCM UpdateInvoiceAttributesPath is not configured. Skipping update. RunId={RunId} CorrelationId={CorrelationId}",
                ctx.RunId, ctx.CorrelationId);
            return new FscmInvoiceAttributesUpdateResult(true, 200, string.Empty);
        }

        updates ??= Array.Empty<InvoiceAttributePair>();

        var url = _payloadBuilder.BuildUrl(_opt.BaseUrl, _opt.UpdateInvoiceAttributesPath);
        var json = _payloadBuilder.BuildUpdatePayload(
            company,
            subProjectId,
            workOrderGuid,
            workOrderId,
            countryRegionId,
            county,
            state,
            dimensionDisplayValue,
            fsaTaxabilityType,
            fsaWellAge,
            fsaWorkType,
            additionalHeaderFields,
            updates);

        var response = await _transport.PostJsonAsync(
            _http,
            ctx,
            url,
            json,
            operation: "FSCM_INV_ATTR_UPDATE",
            ct).ConfigureAwait(false);

        var ok = (int)response.StatusCode is >= 200 and <= 299;
        if (!ok)
            _log.LogWarning("FSCM invoice attributes update failed. Status={Status} Body={Body}", (int)response.StatusCode, LogText.TrimForLog(response.Body));
        else
            _log.LogInformation("FSCM invoice attributes update succeeded. UpdatedCount={Count} ElapsedMs={ElapsedMs}", updates.Count, response.ElapsedMs);

        return new FscmInvoiceAttributesUpdateResult(ok, (int)response.StatusCode, response.Body);
    }
}
