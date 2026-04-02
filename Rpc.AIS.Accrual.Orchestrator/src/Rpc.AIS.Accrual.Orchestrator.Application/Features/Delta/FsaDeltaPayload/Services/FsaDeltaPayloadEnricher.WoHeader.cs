// File: .../FsaDeltaPayloadEnricher.WoHeader.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.Enrichment;

using static Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.FsaDeltaPayloadJsonUtil;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload;

public sealed partial class FsaDeltaPayloadEnricher
{
    public string InjectWorkOrderHeaderFieldsIntoPayload(
            string payloadJson,
            IReadOnlyDictionary<Guid, WoHeaderMappingFields> woIdToHeaderFields)
    {
        return _woHeader.InjectWorkOrderHeaderFieldsIntoPayload(payloadJson, woIdToHeaderFields);
    }

    internal static void CopyRootWithWoHeaderFieldsInjection(
            JsonElement root,
            Utf8JsonWriter w,
            IReadOnlyDictionary<Guid, WoHeaderMappingFields> woIdToHeaderFields)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            root.WriteTo(w);
            return;
        }

        w.WriteStartObject();

        foreach (var p in root.EnumerateObject())
        {
            if (p.NameEquals("_request") && p.Value.ValueKind == JsonValueKind.Object)
            {
                w.WritePropertyName(p.Name);
                CopyRequestWithWoHeaderFieldsInjection(p.Value, w, woIdToHeaderFields);
            }
            else
            {
                w.WritePropertyName(p.Name);
                p.Value.WriteTo(w);
            }
        }

        w.WriteEndObject();
    }

    private static void CopyRequestWithWoHeaderFieldsInjection(
            JsonElement req,
            Utf8JsonWriter w,
            IReadOnlyDictionary<Guid, WoHeaderMappingFields> woIdToHeaderFields)
    {
        w.WriteStartObject();

        foreach (var p in req.EnumerateObject())
        {
            if (p.NameEquals("WOList") && p.Value.ValueKind == JsonValueKind.Array)
            {
                w.WritePropertyName("WOList");
                w.WriteStartArray();

                foreach (var wo in p.Value.EnumerateArray())
                    CopyWoWithWoHeaderFieldsInjection(wo, w, woIdToHeaderFields);

                w.WriteEndArray();
            }
            else
            {
                w.WritePropertyName(p.Name);
                p.Value.WriteTo(w);
            }
        }

        w.WriteEndObject();
    }

    static string PreferHeaderString(JsonElement v, string? headerValue)
    {
        var existing = v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";
        if (!string.IsNullOrWhiteSpace(existing)) return existing;
        return string.IsNullOrWhiteSpace(headerValue) ? "" : headerValue!;
    }

    static string PreferHeaderStringMissing(string? headerValue)
        => string.IsNullOrWhiteSpace(headerValue) ? "" : headerValue!;

    static decimal PreferHeaderDecimal(JsonElement v, decimal? headerValue)
    {
        // keep existing numeric if non-zero
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) && d != 0m) return d;
        if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), out var ds) && ds != 0m) return ds;

        return headerValue ?? 0m;
    }

    // FSCM requires a /Date(ms)/ literal; when FS provides no value we must NOT send blank.
    // Use 1900-01-01T00:00:00Z (Unix ms: -2208988800000) as the agreed "empty" sentinel.
    private const string MissingDateSentinel = "/Date(-2208988800000)/";

    static string ToFscmDateLiteralOrEmpty(DateTime? dtUtc)
    {
        if (!dtUtc.HasValue) return MissingDateSentinel;
        var utc = dtUtc.Value.Kind == DateTimeKind.Utc
            ? dtUtc.Value
            : DateTime.SpecifyKind(dtUtc.Value, DateTimeKind.Utc);
        var ms = new DateTimeOffset(utc).ToUnixTimeMilliseconds();
        return $"/Date({ms})/";
    }
}
