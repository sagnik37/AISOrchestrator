using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;


public sealed class VirtualLookupNameResolver : IVirtualLookupNameResolver
{
    private readonly HttpClient _http;
    private readonly ILogger<VirtualLookupNameResolver> _log;
    private readonly IFsaODataQueryBuilder _qb;
    private readonly IODataPagedReader _reader;

    public VirtualLookupNameResolver(
        HttpClient http,
        ILogger<VirtualLookupNameResolver> log,
        IFsaODataQueryBuilder qb,
        IODataPagedReader reader)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _qb = qb ?? throw new ArgumentNullException(nameof(qb));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }
    public Task<JsonDocument> EnrichLinesWithVirtualLookupNamesAsync(
        HttpClient http,
        JsonDocument baseLines,
        CancellationToken ct)
    {
        
        // - NO virtual entity calls
        // - Read FormattedValue directly from Dataverse response

        if (!TryGetValueArray(baseLines, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Task.FromResult(baseLines);

        using var buffer = new MemoryStream();
        using var writer = new Utf8JsonWriter(buffer);

        writer.WriteStartObject();
        writer.WritePropertyName("value");
        writer.WriteStartArray();

        foreach (var row in arr.EnumerateArray())
        {
            writer.WriteStartObject();

            foreach (var p in row.EnumerateObject())
                p.WriteTo(writer);

            WriteFormatted(row, writer,
                DataverseSchema.Lookup_LineProperty,
                DataverseSchema.FlatLinePropertyNameField);

            WriteFormatted(row, writer,
                DataverseSchema.Lookup_Department,
                DataverseSchema.FlatDepartmentNameField);

            WriteFormatted(row, writer,
                DataverseSchema.Lookup_ProductLine,
                DataverseSchema.FlatProductLineNameField);

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        buffer.Position = 0;
        return Task.FromResult(JsonDocument.Parse(buffer));
    }

    // ---------------- helpers ----------------

    private static void WriteFormatted(
        JsonElement row,
        Utf8JsonWriter writer,
        string lookupLogicalName,
        string flatFieldName)
    {
        var key = lookupLogicalName + DataverseSchema.ODataFormattedSuffix;

        if (row.TryGetProperty(key, out var el) &&
            el.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(el.GetString()))
        {
            writer.WriteString(flatFieldName, el.GetString());
        }
    }

    /// <summary>
    /// Executes try get value array.
    /// </summary>
    private static bool TryGetValueArray(JsonDocument doc, out JsonElement array)
    {
        array = default;
        return doc.RootElement.TryGetProperty("value", out array) &&
               array.ValueKind == JsonValueKind.Array;
    }
}
