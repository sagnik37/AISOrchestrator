// File: .../FsaDeltaPayloadEnricher.SubProject.cs

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
    public string InjectSubProjectIdIntoPayload(
            string payloadJson,
            IReadOnlyDictionary<Guid, string> woIdToSubProjectId)
        {
            return _subProjectId.InjectSubProjectIdIntoPayload(payloadJson, woIdToSubProjectId);
        }

    internal static void CopyRootWithSubProjectIdInjection(
                JsonElement root,
                Utf8JsonWriter w,
                IReadOnlyDictionary<Guid, string> woIdToSubProjectId)
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
                    CopyRequestWithSubProjectIdInjection(p.Value, w, woIdToSubProjectId);
                }
                else
                {
                    w.WritePropertyName(p.Name);
                    p.Value.WriteTo(w);
                }
            }

            w.WriteEndObject();
        }

    private static void CopyRequestWithSubProjectIdInjection(
            JsonElement req,
            Utf8JsonWriter w,
            IReadOnlyDictionary<Guid, string> woIdToSubProjectId)
        {
            w.WriteStartObject();

            foreach (var p in req.EnumerateObject())
            {
                if (p.NameEquals("WOList") && p.Value.ValueKind == JsonValueKind.Array)
                {
                    w.WritePropertyName("WOList");
                    w.WriteStartArray();

                    foreach (var wo in p.Value.EnumerateArray())
                        CopyWoWithSubProjectIdInjection(wo, w, woIdToSubProjectId);

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

    private static void CopyWoWithSubProjectIdInjection(
            JsonElement wo,
            Utf8JsonWriter w,
            IReadOnlyDictionary<Guid, string> woIdToSubProjectId)
        {
            Guid? woId = null;

            if (wo.ValueKind == JsonValueKind.Object)
            {
                if (wo.TryGetProperty("WorkOrderGUID", out var g1) && g1.ValueKind == JsonValueKind.String)
                    woId = ParseGuidLoose(g1.GetString());
                else if (wo.TryGetProperty("WorkorderGUID", out var g2) && g2.ValueKind == JsonValueKind.String)
                    woId = ParseGuidLoose(g2.GetString());
            }

            string? subProjectId = null;
            var hasSubProject = woId.HasValue && woIdToSubProjectId.TryGetValue(woId.Value, out subProjectId);

            w.WriteStartObject();

            var wrote = false;

            foreach (var p in wo.EnumerateObject())
            {
                // Header-only field name:
                if (p.NameEquals("SubProjectId"))
                {
                    var existing = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
                    var final = (string.IsNullOrWhiteSpace(existing) && hasSubProject) ? subProjectId : existing;

                    w.WritePropertyName("SubProjectId");
                    if (final is null) w.WriteNullValue();
                    else w.WriteStringValue(final);

                    wrote = true;
                    continue;
                }

                w.WritePropertyName(p.Name);
                p.Value.WriteTo(w);
            }

            if (!wrote && hasSubProject && !string.IsNullOrWhiteSpace(subProjectId))
                w.WriteString("SubProjectId", subProjectId);

            w.WriteEndObject();
        }

    private static Dictionary<Guid, string> BuildWorkOrderSubProjectIdMap(JsonDocument woHeaders)
        {
            var map = new Dictionary<Guid, string>();

            if (woHeaders is null)
                return map;

            if (!woHeaders.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return map;

            foreach (var row in arr.EnumerateArray())
            {
                if (!TryGuid(row, "msdyn_workorderid", out var woId))
                    continue;

                // SubProject lookup is selected as "_rpc_subproject_value".
                // Requirement: use @FormattedValue only (do NOT resolve GUIDs).
                string? subProjectId = null;
                TryFormattedOnly(row, "_rpc_subproject_value", out subProjectId);

                if (!string.IsNullOrWhiteSpace(subProjectId))
                    map[woId] = subProjectId!;
            }

            return map;

            //static string? TryGetString(JsonElement obj, string prop)
            //    => obj.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

            //static string? TryGetNestedString(JsonElement root, string nestedObjProp, string nestedStringProp)
            //{
            //    if (!root.TryGetProperty(nestedObjProp, out var nested) || nested.ValueKind != JsonValueKind.Object)
            //        return null;

            //    return nested.TryGetProperty(nestedStringProp, out var p) && p.ValueKind == JsonValueKind.String
            //        ? p.GetString()
            //        : null;
            //}
        }
}
