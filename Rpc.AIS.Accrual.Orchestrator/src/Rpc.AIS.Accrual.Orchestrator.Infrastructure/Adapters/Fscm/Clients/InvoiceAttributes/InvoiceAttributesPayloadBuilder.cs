using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.InvoiceAttributes;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

public sealed class InvoiceAttributesPayloadBuilder : IInvoiceAttributesPayloadBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public string BuildUrl(string baseUrl, string path)
    {
        var b = (baseUrl ?? string.Empty).TrimEnd('/');
        var p = (path ?? string.Empty).TrimStart('/');
        return $"{b}/{p}";
    }

    public string BuildDefinitionsPayload(string company, string subProjectId)
        => JsonSerializer.Serialize(new
        {
            Company = company,
            SubProjectId = subProjectId
        }, JsonOptions);

    public string BuildCurrentValuesPayload(string company, string subProjectId, IReadOnlyList<string> attributeNames)
        => JsonSerializer.Serialize(new
        {
            Company = company,
            SubProjectId = subProjectId,
            AttributeNames = attributeNames ?? Array.Empty<string>()
        }, JsonOptions);

    public string BuildUpdatePayload(
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
        IReadOnlyList<InvoiceAttributePair> updates)
    {
        var woNode = new JsonObject
        {
            ["Company"] = company,
            ["SubProjectId"] = subProjectId,
            ["WorkOrderGUID"] = workOrderGuid.ToString("B").ToUpperInvariant(),
            ["WorkOrderID"] = workOrderId,
            ["CountryRegionId"] = countryRegionId ?? string.Empty,
            ["County"] = county ?? string.Empty,
            ["State"] = state ?? string.Empty,
            ["DimensionDisplayValue"] = dimensionDisplayValue ?? string.Empty,
            ["FSATaxabilityType"] = fsaTaxabilityType ?? string.Empty,
            ["FSAWellAge"] = fsaWellAge ?? string.Empty,
            ["FSAWorkType"] = fsaWorkType ?? string.Empty
        };

        if (additionalHeaderFields is not null)
        {
            foreach (var kvp in additionalHeaderFields)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key) || woNode.ContainsKey(kvp.Key))
                    continue;

                woNode[kvp.Key] = kvp.Value switch
                {
                    null => string.Empty,
                    JsonNode node => node,
                    decimal d => JsonValue.Create(d),
                    int i => JsonValue.Create(i),
                    long l => JsonValue.Create(l),
                    double db => JsonValue.Create(db),
                    float f => JsonValue.Create(f),
                    bool b => JsonValue.Create(b),
                    _ => JsonValue.Create(kvp.Value.ToString())
                };
            }
        }

        woNode["InvoiceAttributes"] = new JsonArray(
            (updates ?? Array.Empty<InvoiceAttributePair>())
                .Select(u => (JsonNode)new JsonObject
                {
                    ["AttributeName"] = u.AttributeName,
                    ["AttributeValue"] = u.AttributeValue
                })
                .ToArray());

        var payload = new JsonObject
        {
            ["_request"] = new JsonObject
            {
                ["WOList"] = new JsonArray(woNode)
            }
        };

        return payload.ToJsonString(JsonOptions);
    }
}
