// File: src/Rpc.AIS.Accrual.Orchestrator.Core/Services/FsaDeltaPayload/Json/FsaDeltaPayloadJsonInjector.cs

// File: .../Core/UseCases/FsaDeltaPayload/*
//
// SOLID refactor:
// - Moves delta payload orchestration into Core (UseCase layer) and splits the orchestrator into partials.
// - Functions layer becomes a thin adapter.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;

using static Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.FsaDeltaPayloadJsonUtil;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload;

internal static partial class FsaDeltaPayloadJsonInjector
{
    internal static void CopyRootWithInjectionAndStats(
        JsonElement root,
        Utf8JsonWriter w,
        Dictionary<Guid, FsLineExtras> extrasByLineGuid,
        List<WoEnrichmentStats> stats)
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
                CopyRequestWithInjectionAndStats(p.Value, w, extrasByLineGuid, stats);
            }
            else
            {
                w.WritePropertyName(p.Name);
                p.Value.WriteTo(w);
            }
        }

        w.WriteEndObject();
    }

    private static void CopyRequestWithInjectionAndStats(
        JsonElement req,
        Utf8JsonWriter w,
        Dictionary<Guid, FsLineExtras> extrasByLineGuid,
        List<WoEnrichmentStats> stats)
    {
        w.WriteStartObject();

        foreach (var p in req.EnumerateObject())
        {
            if (p.NameEquals("WOList") && p.Value.ValueKind == JsonValueKind.Array)
            {
                w.WritePropertyName("WOList");
                w.WriteStartArray();

                foreach (var wo in p.Value.EnumerateArray())
                {
                    var s = new WoEnrichmentStats
                    {
                        WorkorderId = ReadWoIdText(wo),
                        WorkorderGuidRaw = ReadWoGuidText(wo),
                        Company = wo.TryGetProperty("Company", out var comp) && comp.ValueKind == JsonValueKind.String ? comp.GetString() : null
                    };

                    CopyWoWithInjectionAndStats(wo, w, extrasByLineGuid, s);
                    stats.Add(s);
                }

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

    private static void CopyWoWithInjectionAndStats(
        JsonElement wo,
        Utf8JsonWriter w,
        Dictionary<Guid, FsLineExtras> extrasByLineGuid,
        WoEnrichmentStats s)
    {
        w.WriteStartObject();

        foreach (var p in wo.EnumerateObject())
        {
            if (p.NameEquals("WOExpLines") || p.NameEquals("WOItemLines") || p.NameEquals("WOHourLines"))
            {
                w.WritePropertyName(p.Name);

                if (p.Value.ValueKind == JsonValueKind.Object)
                {
                    var group = p.Name;
                    CopyWoLinesBlockWithInjectionAndStats(p.Value, w, extrasByLineGuid, s, group);
                }
                else
                {
                    p.Value.WriteTo(w);
                }

                continue;
            }

            w.WritePropertyName(p.Name);
            p.Value.WriteTo(w);
        }

        w.WriteEndObject();
    }

    private static void CopyWoLinesBlockWithInjectionAndStats(
        JsonElement block,
        Utf8JsonWriter w,
        Dictionary<Guid, FsLineExtras> extrasByLineGuid,
        WoEnrichmentStats s,
        string groupName)
    {
        w.WriteStartObject();

        foreach (var p in block.EnumerateObject())
        {
            if (p.NameEquals("JournalLines") && p.Value.ValueKind == JsonValueKind.Array)
            {
                w.WritePropertyName("JournalLines");
                w.WriteStartArray();

                foreach (var line in p.Value.EnumerateArray())
                {
                    var enrichedThisLine = CopyJournalLineWithInjectionAndStats(line, w, extrasByLineGuid, s);

                    if (enrichedThisLine)
                    {
                        s.EnrichedLinesTotal++;

                        if (groupName == "WO Hour Lines") s.EnrichedHourLines++;
                        else if (groupName == "WO Exp Lines") s.EnrichedExpLines++;
                        else if (groupName == "WO Item Lines") s.EnrichedItemLines++;
                    }
                }

                w.WriteEndArray();
                continue;
            }

            w.WritePropertyName(p.Name);
            p.Value.WriteTo(w);
        }

        w.WriteEndObject();
    }
    private static bool TryGetString(JsonElement obj, string property, out string? value)
    {
        value = null;

        if (obj.ValueKind != JsonValueKind.Object)
            return false;

        if (!obj.TryGetProperty(property, out var prop))
            return false;

        if (prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString();
            return true;
        }

        return false;
    }

    private static string? ReadWoIdText(JsonElement wo)
    {
        if (TryGetString(wo, "WorkOrderID", out var v1)) return v1;
        if (TryGetString(wo, "WorkorderID", out var v2)) return v2;
        if (TryGetString(wo, "WorkOrderId", out var v3)) return v3;
        return null;
    }

    private static string? ReadWoGuidText(JsonElement wo)
    {
        if (TryGetString(wo, "WorkOrderGUID", out var v1)) return v1;
        if (TryGetString(wo, "WorkorderGUID", out var v2)) return v2;
        if (TryGetString(wo, "WorkOrderGuid", out var v3)) return v3;
        return null;
    }

}
