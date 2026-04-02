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


public sealed class WarehouseSiteEnricher : IWarehouseSiteEnricher
{
    private readonly HttpClient _http;
    private readonly ILogger<WarehouseSiteEnricher> _log;
    private readonly IFsaODataQueryBuilder _qb;
    private readonly IODataPagedReader _reader;

    public WarehouseSiteEnricher(
        HttpClient http,
        ILogger<WarehouseSiteEnricher> log,
        IFsaODataQueryBuilder qb,
        IODataPagedReader reader)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _qb = qb ?? throw new ArgumentNullException(nameof(qb));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    /// <summary>
    /// Executes enrich wop lines from warehouses async.
    /// </summary>
    public async Task<JsonDocument> EnrichWopLinesFromWarehousesAsync(HttpClient http, JsonDocument baseLines, CancellationToken ct)
    {
        // Ported from original partial method (behavior unchanged).
        if (!TryGetValueArray(baseLines, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return baseLines;

        var warehouseIds = new HashSet<Guid>();

        foreach (var row in arr.EnumerateArray())
        {
            if (row.TryGetProperty(DataverseSchema.WorkOrderProductWarehouseLookupField, out var wh) && wh.ValueKind == JsonValueKind.String)
            {
                if (Guid.TryParse(wh.GetString(), out var id) && id != Guid.Empty)
                    warehouseIds.Add(id);
            }
        }

        if (warehouseIds.Count == 0)
            return baseLines;

        var whUrl = _qb.BuildWarehousesByIdsRelative(warehouseIds);

        _log.LogInformation("Dataverse WAREHOUSE lookup query (relative): {RelativeUrl}", whUrl);

        var whDoc = await _reader.ReadAllPagesAsync(http, whUrl, maxPages: 20, logEntityName: "Warehouses", ct: ct).ConfigureAwait(false);

        var map = BuildWarehouseMap(whDoc);

        // Resolve Operational Site -> msdyn_siteid
        var operationalSiteIds = map.Values
            .Where(v => v.OperationalSiteId.HasValue && v.OperationalSiteId.Value != Guid.Empty)
            .Select(v => v.OperationalSiteId!.Value)
            .Distinct()
            .ToArray();

        if (operationalSiteIds.Length > 0)
        {
            var osUrl = _qb.BuildOperationalSitesByIdsRelative(operationalSiteIds);
            _log.LogInformation("Dataverse OPERATIONAL SITE lookup query (relative): {RelativeUrl}", osUrl);

            var osDoc = await _reader.ReadAllPagesAsync(http, osUrl, maxPages: 20, logEntityName: "OperationalSites", ct: ct).ConfigureAwait(false);
            var osMap = BuildOperationalSiteMap(osDoc);

            var keys = map.Keys.ToArray();
            foreach (var k in keys)
            {
                var info = map[k];
                if (info.OperationalSiteId.HasValue && osMap.TryGetValue(info.OperationalSiteId.Value, out var siteId))
                    map[k] = info with { SiteId = siteId };
            }
        }

        return EnrichRows(baseLines, row =>
        {
            if (!row.TryGetProperty(DataverseSchema.WorkOrderProductWarehouseLookupField, out var wh) || wh.ValueKind != JsonValueKind.String)
                return Array.Empty<(string Name, string? Value)>();

            if (!Guid.TryParse(wh.GetString(), out var id) || id == Guid.Empty)
                return Array.Empty<(string Name, string? Value)>();

            if (!map.TryGetValue(id, out var info))
                return Array.Empty<(string Name, string? Value)>();

            var extras = new List<(string Name, string? Value)>();

            // msdyn_warehouseidentifier (raw field)
            extras.Add((DataverseSchema.WarehouseIdentifierField, info.Identifier));

            // alias fields
            extras.Add((DataverseSchema.PayloadWarehouseField, info.Identifier));
            if (!string.IsNullOrWhiteSpace(info.SiteId))
            {
                extras.Add((DataverseSchema.FlatSiteIdField, info.SiteId));
                extras.Add((DataverseSchema.PayloadSiteField, info.SiteId));
            }

            return extras;
        });
    }

    /// <summary>
    /// Carries warehouse info data.
    /// </summary>
    private sealed record WarehouseInfo(string? Identifier, Guid? OperationalSiteId, string? SiteId);

    /// <summary>
    /// Executes build warehouse map.
    /// </summary>
    private Dictionary<Guid, WarehouseInfo> BuildWarehouseMap(JsonDocument whDoc)
    {
        var map = new Dictionary<Guid, WarehouseInfo>();

        if (!TryGetValueArray(whDoc, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return map;

        foreach (var row in arr.EnumerateArray())
        {
            if (!row.TryGetProperty(DataverseSchema.WarehouseIdAttribute, out var idEl) || idEl.ValueKind != JsonValueKind.String)
                continue;

            if (!Guid.TryParse(idEl.GetString(), out var id) || id == Guid.Empty)
                continue;

            var identifier = row.TryGetProperty(DataverseSchema.WarehouseIdentifierField, out var identEl) && identEl.ValueKind == JsonValueKind.String
                ? identEl.GetString()
                : null;

            Guid? operationalSiteId = null;
            if (row.TryGetProperty(DataverseSchema.WarehouseOperationalSiteLookupField, out var osEl) && osEl.ValueKind == JsonValueKind.String)
            {
                if (Guid.TryParse(osEl.GetString(), out var osId) && osId != Guid.Empty)
                    operationalSiteId = osId;
            }

            map[id] = new WarehouseInfo(identifier, operationalSiteId, SiteId: null);
        }

        return map;
    }
    /// <summary>
    /// Executes build operational site map.
    /// </summary>
    private Dictionary<Guid, string> BuildOperationalSiteMap(JsonDocument osDoc)
    {
        var map = new Dictionary<Guid, string>();

        if (!TryGetValueArray(osDoc, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return map;

        foreach (var row in arr.EnumerateArray())
        {
            if (!row.TryGetProperty(DataverseSchema.OperationalSiteIdAttribute, out var idEl) || idEl.ValueKind != JsonValueKind.String)
                continue;

            if (!Guid.TryParse(idEl.GetString(), out var id) || id == Guid.Empty)
                continue;

            var siteId = row.TryGetProperty(DataverseSchema.OperationalSiteSiteIdField, out var siteEl) && siteEl.ValueKind == JsonValueKind.String
                ? siteEl.GetString()
                : null;

            if (!string.IsNullOrWhiteSpace(siteId))
                map[id] = siteId!;
        }

        return map;
    }


    /// <summary>
    /// Executes try get value array.
    /// </summary>
    private static bool TryGetValueArray(JsonDocument doc, out JsonElement array)
    {
        array = default;
        return doc.RootElement.TryGetProperty("value", out array) && array.ValueKind == JsonValueKind.Array;
    }

    /// <summary>
    /// Executes enrich rows.
    /// </summary>
    private static JsonDocument EnrichRows(JsonDocument baseDoc, Func<JsonElement, IEnumerable<(string Name, string? Value)>> extraFactory)
    {
        if (!TryGetValueArray(baseDoc, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return baseDoc;

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

            foreach (var (Name, Value) in extraFactory(row))
            {
                if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Value))
                    continue;

                if (row.TryGetProperty(Name, out _))
                    continue;

                writer.WriteString(Name, Value);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        buffer.Position = 0;
        return JsonDocument.Parse(buffer);
    }
}
