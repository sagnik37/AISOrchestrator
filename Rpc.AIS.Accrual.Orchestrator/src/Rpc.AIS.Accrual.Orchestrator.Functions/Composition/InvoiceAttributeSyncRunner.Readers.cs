using System;
using System.Collections.Generic;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services;

public sealed partial class InvoiceAttributeSyncRunner
{
    private bool TryReadWorkOrders(RunContext ctx, string payloadJson, out List<WoCtx> workOrders)
    {
        workOrders = new List<WoCtx>();

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (!doc.RootElement.TryGetProperty("_request", out var req) || req.ValueKind != JsonValueKind.Object)
                return false;

            if (!req.TryGetProperty("WOList", out var list) || list.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var wo in list.EnumerateArray())
            {
                if (wo.ValueKind != JsonValueKind.Object) continue;

                var company = TryReadString(wo, "Company") ?? TryReadString(wo, "company");
                var subProjectId = TryReadString(wo, "SubProjectId") ?? TryReadString(wo, "subProjectId");
                var woId = TryReadString(wo, "WorkOrderID") ?? TryReadString(wo, "WorkOrderId") ?? TryReadString(wo, "WONumber") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(subProjectId))
                    continue;

                if (!TryReadGuid(wo, "WorkOrderGUID", out var woGuid))
                    continue;

                workOrders.Add(new WoCtx(woGuid, woId, company.Trim(), subProjectId.Trim()));
            }

            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "InvoiceAttributeSyncRunner: failed to parse posting payload JSON. RunId={RunId} CorrelationId={CorrelationId}. Skipping invoice attribute sync for this payload.",
                ctx.RunId,
                ctx.CorrelationId);
            return false;
        }
    }

    private static bool TryReadGuid(JsonElement obj, string prop, out Guid guid)
    {
        guid = Guid.Empty;
        if (!obj.TryGetProperty(prop, out var p)) return false;

        var s = p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString();
        if (string.IsNullOrWhiteSpace(s)) return false;

        s = s.Trim();
        if (s.StartsWith("{") && s.EndsWith("}"))
            s = s.Trim('{', '}');

        return Guid.TryParse(s, out guid);
    }

    private static string? TryReadString(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var p)) return null;
        if (p.ValueKind == JsonValueKind.String) return p.GetString();
        if (p.ValueKind == JsonValueKind.Null) return null;
        return p.ToString();
    }

    private static Dictionary<Guid, JsonElement> IndexWorkOrderHeaders(JsonDocument woHeadersDoc, ILogger log, RunContext ctx)
    {
        var dict = new Dictionary<Guid, JsonElement>();
        try
        {
            if (!woHeadersDoc.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
                return dict;

            foreach (var el in value.EnumerateArray())
            {
                if (!el.TryGetProperty("msdyn_workorderid", out var idProp)) continue;
                var idStr = idProp.ValueKind == JsonValueKind.String ? idProp.GetString() : idProp.ToString();
                if (!Guid.TryParse(idStr, out var id)) continue;
                dict[id] = el;
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "InvoiceAttributes.Enrich: Failed to index work order headers (Dataverse payload parse). RunId={RunId} CorrelationId={CorrelationId}", ctx.RunId, ctx.CorrelationId);
        }

        return dict;
    }

    private static Dictionary<string, string?> ExtractFsAttributes(JsonElement woHeader)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in FsInvoiceKeys)
        {
            var val = TryReadValueOrLookupFormatted(woHeader, key);
            if (!val.IsPresent)
                continue;

            dict[key] = val.Value;
        }

        return dict;
    }

    private static void AddWorkTypeAndWellAgeDerived(JsonElement woHeader, Dictionary<string, string?> fsAttrsRaw)
    {
        // Work Type / Well Age are now sourced from rpc_worktype fields (rpc_welltype / rpc_wellage)
        // and injected onto the WO header by FsaLineFetcherWorkflow.
        // We simply copy the already-enriched values into the fixed FSCM comparisons.

        if (woHeader.TryGetProperty("Work Type", out var wt) && wt.ValueKind == JsonValueKind.String)
        {
            var workType = wt.GetString();
            if (!string.IsNullOrWhiteSpace(workType))
                fsAttrsRaw[FscmAttr_WorkType] = workType.Trim();
        }

        // Back-compat: older enrichment used "Well Name" as the work type carrier.
        if (!fsAttrsRaw.ContainsKey(FscmAttr_WorkType) &&
            woHeader.TryGetProperty("Well Name", out var wn) && wn.ValueKind == JsonValueKind.String)
        {
            var workType = wn.GetString();
            if (!string.IsNullOrWhiteSpace(workType))
                fsAttrsRaw[FscmAttr_WorkType] = workType.Trim();
        }

        if (woHeader.TryGetProperty("Well Age", out var wa) && wa.ValueKind == JsonValueKind.String)
        {
            var wellAge = wa.GetString();
            if (!string.IsNullOrWhiteSpace(wellAge))
                fsAttrsRaw[FscmAttr_WellAge] = wellAge.Trim();
        }
    }

    private static Dictionary<Guid, string> BuildTaxabilityTypeByWorkOrder(JsonDocument wopDoc, JsonDocument wosDoc, ILogger log, RunContext ctx)
    {
        var result = new Dictionary<Guid, string>();
        AddFromLines(wopDoc, result, log, ctx);
        AddFromLines(wosDoc, result, log, ctx);
        return result;

        static void AddFromLines(JsonDocument doc, Dictionary<Guid, string> map, ILogger log, RunContext ctx)
        {
            try
            {
                if (!doc.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
                    return;

                foreach (var row in value.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object) continue;

                    if (!TryReadGuidLookup(row, "_msdyn_workorder_value", out var woGuid))
                        continue;

                    if (map.ContainsKey(woGuid))
                        continue;

                    if (!row.TryGetProperty("_rpc_operationtype_value", out var op) || op.ValueKind == JsonValueKind.Null)
                        continue;

                    var taxFormatted = TryGetFormattedValue(row, "_rpc_taxabilitytype_value");
                    if (string.IsNullOrWhiteSpace(taxFormatted))
                        continue;

                    map[woGuid] = taxFormatted.Trim();
                }
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "InvoiceAttributes.Enrich: Failed to index taxability type by work order (Dataverse payload parse). RunId={RunId} CorrelationId={CorrelationId}", ctx.RunId, ctx.CorrelationId);
            }
        }
    }

    private static bool TryReadGuidLookup(JsonElement obj, string lookupValueProp, out Guid guid)
    {
        guid = Guid.Empty;
        if (!obj.TryGetProperty(lookupValueProp, out var p)) return false;
        var s = p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString();
        return Guid.TryParse(s, out guid);
    }

    private static string? TryGetFormattedValue(JsonElement obj, string prop)
    {
        var formattedKey = prop + ODataFormattedSuffix;
        if (obj.TryGetProperty(formattedKey, out var fv) && fv.ValueKind == JsonValueKind.String)
            return fv.GetString();
        return null;
    }

    private static PresentOrValue TryReadValueOrLookupFormatted(JsonElement obj, string logicalName)
    {
        var directFormatted = TryGetFormattedValue(obj, logicalName);
        if (!string.IsNullOrWhiteSpace(directFormatted))
            return new PresentOrValue(true, directFormatted);

        if (obj.TryGetProperty(logicalName, out var p))
        {
            if (p.ValueKind == JsonValueKind.Null)
                return new PresentOrValue(true, null);

            return new PresentOrValue(true, p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString());
        }

        var lookupValueProp = "_" + logicalName + "_value";
        if (obj.TryGetProperty(lookupValueProp, out var lp))
        {
            var formatted = TryGetFormattedValue(obj, lookupValueProp);
            if (!string.IsNullOrWhiteSpace(formatted))
                return new PresentOrValue(true, formatted);

            if (lp.ValueKind == JsonValueKind.Null)
                return new PresentOrValue(true, null);

            return new PresentOrValue(true, lp.ValueKind == JsonValueKind.String ? lp.GetString() : lp.ToString());
        }

        return new PresentOrValue(false, null);
    }

    private readonly struct PresentOrValue
    {
        public bool IsPresent { get; }
        public string? Value { get; }

        public PresentOrValue(bool isPresent, string? value)
        {
            IsPresent = isPresent;
            Value = value;
        }
    }
}

