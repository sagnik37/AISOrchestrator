// File: Rpc.AIS.Accrual.Orchestrator/src/Rpc.AIS.Accrual.Orchestrator.Core/Services/DeltaPayloadBuilder.cs
//
// (FULL FILE CONTENT)
// Behavioral changes applied:
// - FSAUnitPrice comes ONLY from l.FsaUnitPrice (msdyn_unitamount). No rpc_calculatedunitprice fallback.
// - UnitAmount remains l.UnitAmount (now also msdyn_unitamount via mapper).
//
// Critical fix (empty payload bug):
// - When caller passes FsaDeltaSnapshot, product lines live under:
//      InventoryProducts  -> Item journal lines
//      NonInventoryProducts -> Expense journal lines
//   So we must include those property names in reflection lookup.
//
// Compile/architecture fixes applied:
// - No dependency on missing DeltaWorkOrderSnapshot: Build<TWorkOrder>() + reflection helpers.
// - Added BuildWoListPayload(...) (string) for existing callers.
// - Fixed CS8116 by removing illegal nullable-pattern matching for decimal? and DateTime?.
//
// NEW FIX (Warehouse rules):
// - Warehouse is only applicable for Item journal lines.
// - Expense journal payload MUST NOT include Warehouse at all.
// - Hour journal payload MUST NOT include Warehouse at all (already the case).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services;

public static partial class DeltaPayloadBuilder
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    /// <summary>
    /// Existing use-case contract: build outbound payload JSON as string.
    /// </summary>
    public static string BuildWoListPayload(
        IReadOnlyList<FsaDeltaSnapshot> snapshots,
        string correlationId,
        string runId,
        string system = "FieldService",
        string? triggeredByOverride = null)
    {
        var json = Build(system, runId, correlationId, snapshots, triggeredByOverride);
        return json.ToJsonString(JsonOpts);
    }

    /// <summary>
    /// Generic to avoid a compile-time dependency on a specific snapshot type name.
    /// Caller can pass IReadOnlyList&lt;AnySnapshotType&gt; and this will reflect required properties.
    /// </summary>
    public static JsonObject Build<TWorkOrder>(
        string system,
        string runId,
        string correlationId,
        IReadOnlyList<TWorkOrder> workOrders,
        string? triggeredByOverride = null)
        where TWorkOrder : class
    {
        var woList = new JsonArray();

        foreach (var wo in workOrders)
        {
            woList.Add(BuildWorkOrder(system, runId, correlationId, wo, triggeredByOverride));
        }

        return new JsonObject
        {
            ["_request"] = new JsonObject
            {
                ["System"] = system,
                ["RunId"] = runId,
                ["CorrelationId"] = correlationId,
                ["WOList"] = woList
            }
        };
    }

    private static JsonObject BuildWorkOrder<TWorkOrder>(string system, string runId, string correlationId, TWorkOrder wo, string? triggeredByOverride)
        where TWorkOrder : class
    {
        var jobId = GetStringProp(wo,
                        "WorkOrderNumber", "WorkOrderNo", "WorkOrderID", "WorkOrderIdText", "msdyn_name")
                    ?? string.Empty;

        var subProjectId = GetStringProp(wo, "SubProjectId", "SubProjectID", "rpc_subprojectid", "SubProject")
                           ?? string.Empty;

        var woGuid = GetGuidProp(wo, "WorkOrderId", "WorkOrderGuid", "WorkOrderGUID", "WorkOrderIDGuid", "msdyn_workorderid");

        var triggeredBy = GetStringProp(wo, "TriggeredBy", "Trigger", "Triggered", "Source", "InvocationSource")
                         ?? triggeredByOverride
                         ?? string.Empty;
        var action = ResolveJournalActionSuffixForTriggeredBy(triggeredBy);
        var journalDescription = BuildJournalDescription(jobId, subProjectId, action);

        var obj = new JsonObject
        {
            ["WorkOrderGUID"] = ToBracedUpperGuidString(woGuid),
            ["WorkOrderID"] = jobId,
            ["Company"] = GetStringProp(wo, "Company", "LegalEntity", "DataAreaId", "dataAreaId") ?? string.Empty,
            ["SubProjectId"] = subProjectId,
            ["CountryRegionId"] = GetStringProp(wo, "CountryRegionId", "Country", "CountryRegion") ?? string.Empty,
            ["County"] = GetStringProp(wo, "County") ?? string.Empty,
            ["State"] = GetStringProp(wo, "State") ?? string.Empty,
            ["DimensionDisplayValue"] = GetStringProp(wo, "DimensionDisplayValue", "DefaultDimensionDisplayValue") ?? string.Empty,
            ["FSATaxabilityType"] = GetStringProp(wo, "FSATaxabilityType", "TaxabilityType") ?? string.Empty,
            ["FSAWellAge"] = GetStringProp(wo, "FSAWellAge", "WellAge") ?? string.Empty,
            ["FSAWorkType"] = GetStringProp(wo, "FSAWorkType", "WorkType") ?? string.Empty
        };

        var itemLines = GetLines<FsaProductLine>(
            wo,
            "ItemLines", "WOItemLines", "ProductLines", "ItemJournalLines",
            "InventoryProducts", "InventoryLines", "InventoryProductLines");

        if (itemLines is { Count: > 0 })
        {
            obj["WOItemLines"] = BuildItemJournal(itemLines, journalDescription);
        }

        var expenseLines = GetLines<FsaProductLine>(
            wo,
            "ExpenseLines", "WOExpLines", "ExpenseJournalLines",
            "NonInventoryProducts", "NonInventoryLines", "NonInventoryProductLines");

        if (expenseLines is { Count: > 0 })
        {
            obj["WOExpLines"] = BuildExpenseJournal(expenseLines, journalDescription);
        }

        var hourLines = GetLines<FsaServiceLine>(
            wo,
            "HourLines", "WOHourLines", "ServiceLines", "HourJournalLines");

        if (hourLines is { Count: > 0 })
        {
            obj["WOHourLines"] = BuildHourJournal(hourLines, journalDescription);
        }

        return obj;
    }
}
