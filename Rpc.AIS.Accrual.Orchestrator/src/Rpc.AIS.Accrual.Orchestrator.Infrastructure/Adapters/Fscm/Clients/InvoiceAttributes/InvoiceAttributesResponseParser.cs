using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.InvoiceAttributes;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

public sealed class InvoiceAttributesResponseParser : IInvoiceAttributesResponseParser
{
    private readonly ILogger<InvoiceAttributesResponseParser> _log;

    public InvoiceAttributesResponseParser(ILogger<InvoiceAttributesResponseParser> log)
        => _log = log ?? throw new ArgumentNullException(nameof(log));

    public IReadOnlyList<InvoiceAttributeDefinition> ParseDefinitions(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return Array.Empty<InvoiceAttributeDefinition>();

        try
        {
            using var doc = JsonDocument.Parse(body);

            JsonElement arr;
            if ((doc.RootElement.TryGetProperty("InvoiceAttributeDefinitions", out arr)
                 || doc.RootElement.TryGetProperty("attributeDefinitions", out arr))
                && arr.ValueKind == JsonValueKind.Array)
            {
                var defs = new List<InvoiceAttributeDefinition>();
                foreach (var el in arr.EnumerateArray())
                {
                    var name = el.TryGetProperty("AttributeName", out var an) ? an.GetString() :
                               el.TryGetProperty("name", out var n) ? n.GetString() : null;

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    var type = el.TryGetProperty("Type", out var t) ? t.GetString() :
                               el.TryGetProperty("type", out var t2) ? t2.GetString() : null;

                    var active = el.TryGetProperty("Active", out var a) ? a.GetBoolean() :
                                 el.TryGetProperty("active", out var a2) ? a2.GetBoolean() : true;

                    defs.Add(new InvoiceAttributeDefinition(name!, type, active));
                }

                return defs;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to parse FSCM attribute definitions response. Treating as empty.");
        }

        return Array.Empty<InvoiceAttributeDefinition>();
    }

    public IReadOnlyList<InvoiceAttributePair> ParseCurrentValues(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return Array.Empty<InvoiceAttributePair>();

        try
        {
            using var doc = JsonDocument.Parse(body);

            JsonElement arr;
            if (doc.RootElement.TryGetProperty("InvoiceAttributes", out arr)
                || doc.RootElement.TryGetProperty("invoiceAttributes", out arr)
                || doc.RootElement.TryGetProperty("attributes", out arr))
            {
                if (arr.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<InvoiceAttributePair>();
                    foreach (var el in arr.EnumerateArray())
                    {
                        var name = el.TryGetProperty("AttributeName", out var n) ? n.GetString() :
                                   el.TryGetProperty("name", out var n2) ? n2.GetString() : null;

                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        var val = el.TryGetProperty("AttributeValue", out var v) ? v.ToString() :
                                  el.TryGetProperty("value", out var v2) ? v2.ToString() : null;

                        list.Add(new InvoiceAttributePair(name!, val));
                    }

                    return list;
                }

                if (arr.ValueKind == JsonValueKind.Object)
                {
                    var list = new List<InvoiceAttributePair>();
                    foreach (var p in arr.EnumerateObject())
                        list.Add(new InvoiceAttributePair(p.Name, p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.ToString()));

                    return list;
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to parse FSCM attribute values response. Treating as empty.");
        }

        return Array.Empty<InvoiceAttributePair>();
    }
}
