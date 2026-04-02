// File: .../FsaDeltaPayloadEnricher.Company.cs

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
    public string InjectCompanyIntoPayload(
            string payloadJson,
            IReadOnlyDictionary<Guid, string> woIdToCompanyName)
        {
            return _company.InjectCompanyIntoPayload(payloadJson, woIdToCompanyName);
        }

    internal static void CopyRootWithCompanyInjection(
            JsonElement root,
            Utf8JsonWriter w,
            IReadOnlyDictionary<Guid, string> woIdToCompanyName)
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
                    CopyRequestWithCompanyInjection(p.Value, w, woIdToCompanyName);
                }
                else
                {
                    w.WritePropertyName(p.Name);
                    p.Value.WriteTo(w);
                }
            }

            w.WriteEndObject();
        }

    private static void CopyRequestWithCompanyInjection(
            JsonElement req,
            Utf8JsonWriter w,
            IReadOnlyDictionary<Guid, string> woIdToCompanyName)
        {
            w.WriteStartObject();

            foreach (var p in req.EnumerateObject())
            {
                if (p.NameEquals("WOList") && p.Value.ValueKind == JsonValueKind.Array)
                {
                    w.WritePropertyName("WOList");
                    w.WriteStartArray();

                    foreach (var wo in p.Value.EnumerateArray())
                        CopyWoWithCompanyInjection(wo, w, woIdToCompanyName);

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

    private static void CopyWoWithCompanyInjection(
            JsonElement wo,
            Utf8JsonWriter w,
            IReadOnlyDictionary<Guid, string> woIdToCompanyName)
        {
            Guid? woId = null;

            if (wo.ValueKind == JsonValueKind.Object)
            {
                if (wo.TryGetProperty("WorkOrderGUID", out var g1) && g1.ValueKind == JsonValueKind.String)
                    woId = ParseGuidLoose(g1.GetString());
                else if (wo.TryGetProperty("WorkorderGUID", out var g2) && g2.ValueKind == JsonValueKind.String)
                    woId = ParseGuidLoose(g2.GetString());
            }

            string? companyName = null;
            var hasCompany = woId.HasValue && woIdToCompanyName.TryGetValue(woId.Value, out companyName);

            w.WriteStartObject();

            var wroteCompany = false;

            foreach (var p in wo.EnumerateObject())
            {
                if (p.NameEquals("Company"))
                {
                    var existing = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
                    var final = (string.IsNullOrWhiteSpace(existing) && hasCompany) ? companyName : existing;

                    w.WritePropertyName("Company");
                    if (final is null) w.WriteNullValue();
                    else w.WriteStringValue(final);

                    wroteCompany = true;
                    continue;
                }

                w.WritePropertyName(p.Name);
                p.Value.WriteTo(w);
            }

            if (!wroteCompany && hasCompany && !string.IsNullOrWhiteSpace(companyName))
                w.WriteString("Company", companyName);

            w.WriteEndObject();
        }

    private static Dictionary<Guid, string> BuildWorkOrderCompanyNameMap(JsonDocument woHeaders)
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

                // Priority order:
                //  1) Flattened by fetcher enrichment: cdm_companycode (string)
                //  2) Nested expand: msdyn_serviceaccount._msdyn_company_value@FormattedValue
                //  3) Legacy fallbacks if present
                var company =
                    TryGetString(row, "cdm_companycode")
                    ?? TryGetNestedFormattedOrRawNonGuid(
                        row,
                        nestedObjProp: "msdyn_serviceaccount",
                        preferredFormattedProp: "_msdyn_company_value@OData.Community.Display.V1.FormattedValue",
                        preferredRawProp: "_msdyn_company_value")
                    ?? TryGetString(row, "msdyn_companyname")
                    ?? TryGetString(row, "msdyn_company");

                if (!string.IsNullOrWhiteSpace(company))
                    map[woId] = company!;
            }

            return map;

            static string? TryGetString(JsonElement obj, string prop)
                => obj.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

            static string? TryGetNestedFormattedOrRawNonGuid(
                JsonElement root,
                string nestedObjProp,
                string preferredFormattedProp,
                string preferredRawProp)
            {
                if (!root.TryGetProperty(nestedObjProp, out var nested) || nested.ValueKind != JsonValueKind.Object)
                    return null;

                // Prefer formatted label
                if (nested.TryGetProperty(preferredFormattedProp, out var f) && f.ValueKind == JsonValueKind.String)
                {
                    var label = f.GetString();
                    if (!string.IsNullOrWhiteSpace(label))
                        return label;
                }

                // Fallback to raw (but ignore GUID-looking values)
                if (nested.TryGetProperty(preferredRawProp, out var r))
                {
                    var raw = r.ValueKind == JsonValueKind.String ? r.GetString() : r.ToString();
                    if (LooksLikeGuid(raw))
                        return null;

                    return string.IsNullOrWhiteSpace(raw) ? null : raw;
                }

                return null;
            }
        }
}
