// File: .../Core/UseCases/FsaDeltaPayload/*
//

// - Moves delta payload orchestration into Core (UseCase layer) and splits the  orchestrator into partials.
// - Functions layer becomes a thin adapter.
//


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
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

internal static class FsaDeltaPayloadWorkOrderHeaderMaps
{
    public static Dictionary<Guid, string> BuildWorkOrderCompanyNameMap(JsonDocument openWorkOrders)
    {
        var map = new Dictionary<Guid, string>();
        if (openWorkOrders is null) return map;

        if (!openWorkOrders.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return map;

        foreach (var row in arr.EnumerateArray())
        {
            if (!TryGuid(row, "msdyn_workorderid", out var woId))
                continue;

            // Preferred: flattened string field
            if (row.TryGetProperty("cdm_companycode", out var cc) && cc.ValueKind == JsonValueKind.String)
            {
                var s = cc.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    map[woId] = s!;
                    continue;
                }
            }

            // Fallback: formatted value of company lookup (if present)
            //  flattener may not have run in some edge paths.
            if (TryFormattedOnly(row, "_msdyn_company_value", out var formatted) && !string.IsNullOrWhiteSpace(formatted))
            {
                map[woId] = formatted!;
            }
        }

        return map;
    }

    public static Dictionary<Guid, string> BuildWorkOrderSubProjectIdMap(JsonDocument openWorkOrders)
    {
        var map = new Dictionary<Guid, string>();
        if (openWorkOrders is null) return map;

        if (!openWorkOrders.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return map;

        foreach (var row in arr.EnumerateArray())
        {
            if (!TryGuid(row, "msdyn_workorderid", out var woId))
                continue;

            // new field first
            if (TryFormattedOnly(row, "rpc_subproject", out var spFmt) && !string.IsNullOrWhiteSpace(spFmt))
            {
                map[woId] = spFmt!;
                continue;
            }

            // common Dataverse lookup raw name variants (if formatted annotation not present)
            if (TryFormattedOnly(row, "_rpc_subproject_value", out spFmt) && !string.IsNullOrWhiteSpace(spFmt))
            {
                map[woId] = spFmt!;
                continue;
            }

            // legacy
            if (TryFormattedOnly(row, "msdyn_fnosubproject", out var legacyFmt) && !string.IsNullOrWhiteSpace(legacyFmt))
            {
                map[woId] = legacyFmt!;
                continue;
            }
            if (TryFormattedOnly(row, "_msdyn_fnosubproject_value", out legacyFmt) && !string.IsNullOrWhiteSpace(legacyFmt))
            {
                map[woId] = legacyFmt!;
            }
        }

        return map;
    }


    public static Dictionary<Guid, WoHeaderMappingFields> BuildWorkOrderHeaderFieldsMap(JsonDocument openWorkOrders)
    {
        var map = new Dictionary<Guid, WoHeaderMappingFields>();
        if (openWorkOrders is null) return map;

        if (!openWorkOrders.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return map;

        foreach (var row in arr.EnumerateArray())
        {
            //var r = row.RootElement.GetRawText();
            if (!TryGuid(row, "msdyn_workorderid", out var woId))
                continue;

            var actualStart = TryGetDateUtcLoose(row, "rpc_utcactualstartdate")
                ?? TryGetDateUtcLoose(row, "msdyn_actualstarttime");
            var actualEnd = TryGetDateUtcLoose(row, "rpc_utcactualenddate")
                ?? TryGetDateUtcLoose(row, "msdyn_actualendtime");
            var projectedStart = TryGetDateUtcLoose(row, "msdyn_timefrompromised");
            var projectedEnd = TryGetDateUtcLoose(row, "msdyn_timetopromised");

            var lat = TryGetDecimalLoose(row, "rpc_welllatitude");
            var lon = TryGetDecimalLoose(row, "rpc_welllongitude");

            var invoiceNotesInternal = TryGetString(row, "rpc_invoicenotesinternal");
            var invoiceNotesExternal = TryGetString(row, "rpc_invoicenotesexternal");
            var poNumber = TryGetString(row, "rpc_ponumber");
            var declined = TryGetString(row, "rpc_declinedtosignreason");

            var worktype = TryGetString(row, "Work Type") ?? TryGetString(row, "Well Name");
            var wellage = TryGetString(row, "Well Age");
            var taxabilitytype = TryGetString(row, "Taxability Type") ?? TryGetString(row, "FSATaxabilityType");

            // Dept / ProductLine at WO header (lookup fields). We store formatted values only.
            TryFormattedOnly(row, "_rpc_departments_value", out var dept);
            TryFormattedOnly(row, "_rpc_productlines_value", out var prod);
            TryFormattedOnly(row, "_rpc_warehouse_value", out var warehouse);
            TryFormattedOnly(row, "_rpc_statelookup_value", out var state);
            TryFormattedOnly(row, "_rpc_countylookup_value", out var county);
            TryFormattedOnly(row, "_rpc_countrylookup_value", out var country);
            map[woId] = new WoHeaderMappingFields(
                ActualStartDateUtc: actualStart,
                ActualEndDateUtc: actualEnd,
                ProjectedStartDateUtc: projectedStart,
                ProjectedEndDateUtc: projectedEnd,
                WellLatitude: lat,
                WellLongitude: lon,
                InvoiceNotesInternal: invoiceNotesInternal,
                InvoiceNotesExternal: invoiceNotesExternal,
                PONumber: poNumber,
                DeclinedToSignReason: declined,
                Department: string.IsNullOrWhiteSpace(dept) ? null : dept,
                ProductLine: string.IsNullOrWhiteSpace(prod) ? null : prod,
                Warehouse: string.IsNullOrWhiteSpace(warehouse) ? null : warehouse,
                FSATaxabilityType: taxabilitytype,
                FSAWellAge: wellage,
                FSAWorkType: worktype,
                Country: country,
                County: county,
                State: state
            );
        }

        return map;
    }

    private static string? TryGetString(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var p)) return null;
        return p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    }

    private static decimal? TryGetDecimalLoose(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var p)) return null;

        if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var d))
            return d;

        if (p.ValueKind == JsonValueKind.String && decimal.TryParse(p.GetString(), out var ds))
            return ds;

        return null;
    }

    private static DateTime? TryGetDateUtcLoose(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var p)) return null;

        if (p.ValueKind == JsonValueKind.String)
        {
            var s = p.GetString();
            if (string.IsNullOrWhiteSpace(s)) return null;

            if (DateTimeOffset.TryParse(s, out var dto))
                return dto.UtcDateTime;

            if (DateTime.TryParse(s, out var dt))
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }

        return null;
    }
}
