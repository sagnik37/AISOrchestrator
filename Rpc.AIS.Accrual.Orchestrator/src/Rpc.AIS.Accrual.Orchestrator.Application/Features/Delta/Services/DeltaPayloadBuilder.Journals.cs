using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services;

public static partial class DeltaPayloadBuilder
{
    private static JsonObject BuildItemJournal(IReadOnlyList<FsaProductLine> lines, string journalDescription)
    {
        return new JsonObject
        {
            ["JournalDescription"] = journalDescription,
            ["LineType"] = "Item",
            ["JournalLines"] = JsonSerializer.SerializeToNode(
                lines.OrderBy(l => l.WorkOrderNumber ?? string.Empty)
                     .ThenBy(l => l.LineId)
                     .Select(l => BuildProductJournalLine(l, journalDescription, includeWarehouse: true))
                     .ToList(),
                JsonOpts)
        };
    }

    private static JsonObject BuildExpenseJournal(IReadOnlyList<FsaProductLine> lines, string journalDescription)
    {
        var list = lines
            .OrderBy(l => l.WorkOrderNumber ?? string.Empty)
            .ThenBy(l => l.LineId)
            .Select(l =>
            {
                var o = BuildProductJournalLine(l, journalDescription, includeWarehouse: false);
                o.Remove("Warehouse");
                o.Remove("Site");
                return o;
            })
            .ToList();

        return new JsonObject
        {
            ["JournalDescription"] = journalDescription,
            ["LineType"] = "Expense",
            ["JournalLines"] = JsonSerializer.SerializeToNode(list, JsonOpts)
        };
    }

    private static JsonObject BuildHourJournal(IReadOnlyList<FsaServiceLine> lines, string journalDescription)
    {
        return new JsonObject
        {
            ["JournalDescription"] = journalDescription,
            ["LineType"] = "Hour",
            ["JournalLines"] = JsonSerializer.SerializeToNode(
                lines.OrderBy(l => l.WorkOrderNumber ?? string.Empty)
                     .ThenBy(l => l.LineId)
                     .Select(l => BuildServiceJournalLine(l, journalDescription))
                     .ToList(),
                JsonOpts)
        };
    }

    private static JsonObject BuildProductJournalLine(FsaProductLine l, string journalDescription, bool includeWarehouse)
    {
        var quantity = l.Quantity ?? 0m;

        var fsaUnitPrice = l.FsaUnitPrice ?? 0m;

        var txDate = l.OperationsDateUtc
                     ?? GetDateTimeProp(l, "TransactionDate", "Transactiondate", "rpc_transactiondate", "Date", "PostedDate");

        var txDateFscm = txDate is null ? string.Empty : ToFscmDateLiteral(txDate.Value);

        var obj = new JsonObject
        {
            ["WorkOrderLineGuid"] = ToBracedUpperGuidString(l.LineId),
            ["Currency"] = S(l.Currency),

            ["DimensionDisplayValue"] = BuildDefaultDimensionDisplayValue(l.Department, l.ProductLine),

            ["FSAUnitPrice"] = fsaUnitPrice,

            ["ItemId"] = S(l.ItemNumber),
            ["ProjectCategory"] = S(l.ProjectCategory),

            ["JournalDescription"] = journalDescription,
            ["JournalLineDescription"] = l.JournalDescription,

            ["LineProperty"] = S(l.LineProperty),

            ["Quantity"] = quantity,

            ["RPCCustomerProductReference"] = S(l.CustomerProductReference),

            ["RPCDiscountAmount"] = l.DiscountAmount ?? 0m,
            ["RPCDiscountPercent"] = l.DiscountPercent ?? 0m,
            ["RPCMarkupPercent"] = GetDecimalProp(l, "MarkupPercent", "RPCMarkupPercent") ?? 0m,
            ["RPCOverallDiscountAmount"] = GetDecimalProp(l, "OverallDiscountAmount", "RPCOverallDiscountAmount") ?? 0m,
            ["RPCOverallDiscountPercent"] = GetDecimalProp(l, "OverallDiscountPercent", "RPCOverallDiscountPercent") ?? 0m,
            ["RPCSurchargeAmount"] = l.SurchargeAmount ?? 0m,
            ["RPCSurchargePercent"] = l.SurchargePercent ?? 0m,
            ["RPMarkUpAmount"] = GetDecimalProp(l, "MarkupAmount", "RPMarkUpAmount") ?? 0m,

            ["TransactionDate"] = txDateFscm,
            ["OperationDate"] = txDateFscm,

            ["UnitAmount"] = (l.CalculatedUnitPrice ?? l.UnitAmount ?? l.FsaUnitPrice) ?? 0m,
            ["IsPrintable"] = (l.Printable ?? false) ? "Yes" : "No",
            ["UnitId"] = S(l.Unit)
        };

        if (includeWarehouse)
        {
            var wh = l.Warehouse;
            if (!string.IsNullOrWhiteSpace(wh))
            {
                obj["Warehouse"] = wh.Trim();
            }
        }

        obj.Remove("Site");
        obj.Remove("ResourceCompany");
        obj.Remove("ResourceId");
        obj.Remove("AccrualLineVersionNumber");
        obj.Remove("ProductColourId");
        obj.Remove("ProductConfigurationId");
        obj.Remove("ProductSizeId");
        obj.Remove("ProductStyleId");

        if (!string.IsNullOrWhiteSpace(l.TaxabilityType))
        {
            obj["FSATaxabilityType"] = S(l.TaxabilityType);
        }

        // Preserve active/inactive state in the outbound FSA payload so the delta layer
        // can correctly identify deactivated FS lines and generate reversals.
        // Existing loose readers honor either IsActive=false or statecode=1.
        obj["IsActive"] = l.IsActive;
        obj["statecode"] = l.IsActive ?? true;

        return obj;
    }

    private static JsonObject BuildServiceJournalLine(FsaServiceLine l, string journalDescription)
    {
        var fsaUnitPrice = l.FsaUnitPrice ?? 0m;

        var txDate = l.OperationsDateUtc
                     ?? GetDateTimeProp(l, "TransactionDate", "Transactiondate", "rpc_transactiondate", "Date", "PostedDate");

        var txDateFscm = txDate is null ? string.Empty : ToFscmDateLiteral(txDate.Value);

        var obj = new JsonObject
        {
            ["WorkOrderLineGuid"] = ToBracedUpperGuidString(l.LineId),
            ["Currency"] = S(l.Currency),

            ["DimensionDisplayValue"] = BuildDefaultDimensionDisplayValue(l.Department, l.ProductLine),

            ["Duration"] = l.Duration ?? 0m,

            ["FSAUnitPrice"] = fsaUnitPrice,

            ["ItemId"] = S(GetStringProp(l, "ItemId", "ItemNumber", "Item", "msdyn_name")),

            ["JournalDescription"] = journalDescription,
            ["JournalLineDescription"] = journalDescription,

            ["LineProperty"] = S(l.LineProperty),

            ["TransactionDate"] = txDateFscm,
            ["OperationDate"] = txDateFscm,

            ["IsPrintable"] = (l.Printable ?? false) ? "Yes" : "No",
            ["UnitAmount"] = (l.CalculatedUnitPrice ?? l.UnitAmount ?? l.FsaUnitPrice) ?? 0m,
            ["UnitId"] = S(l.Unit)
        };

        obj.Remove("Site");
        obj.Remove("ResourceCompany");
        obj.Remove("ResourceId");
        obj.Remove("AccrualLineVersionNumber");
        obj.Remove("ProductColourId");
        obj.Remove("ProductConfigurationId");
        obj.Remove("ProductSizeId");
        obj.Remove("ProductStyleId");

        if (!string.IsNullOrWhiteSpace(l.TaxabilityType))
        {
            obj["FSATaxabilityType"] = S(l.TaxabilityType);
        }

        // Preserve active/inactive state in the outbound FSA payload so the delta layer
        // can correctly identify deactivated FS lines and generate reversals.
        // Existing loose readers honor either IsActive=false or statecode=1.
        obj["IsActive"] = l.IsActive;
        obj["statecode"] = l.IsActive ?? true;

        return obj;
    }

    private static string BuildJournalDescription(string jobId, string subProjectId, string action)
        => $"{S(jobId)} - {S(subProjectId)} - {S(action)}";

    /// <summary>
    /// Resolve the journal action suffix used in JournalDescription based on trigger source.
    /// This is intentionally tolerant of casing and minor variants (e.g., AdhocBulk vs AdHocBulk).
    /// </summary>
    public static string ResolveJournalActionSuffixForTriggeredBy(string? triggeredBy)
    {
        var t = triggeredBy?.Trim();
        if (string.IsNullOrWhiteSpace(t)) return "Post";

        if (t.Equals("Timer", StringComparison.OrdinalIgnoreCase)) return "Create";
        if (t.Equals("AdHocSingle", StringComparison.OrdinalIgnoreCase)) return "Create";
        if (t.Equals("AdHocBulk", StringComparison.OrdinalIgnoreCase) || t.Equals("AdhocBulk", StringComparison.OrdinalIgnoreCase)) return "Create";
        if (t.Equals("AdHocAll", StringComparison.OrdinalIgnoreCase) || t.Equals("AdhocAll", StringComparison.OrdinalIgnoreCase)) return "Create";
        if (t.Equals("CustomerChange", StringComparison.OrdinalIgnoreCase)) return "Billing Location Ch";
        if (t.Equals("Cancel", StringComparison.OrdinalIgnoreCase)) return "Cancel";

        return "Post";
    }

    private static string S(string? s) => s ?? string.Empty;

    private static string ToBracedUpperGuidString(Guid id)
        => "{" + id.ToString().ToUpperInvariant() + "}";

    private static string BuildDefaultDimensionDisplayValue(string? dept, string? prod)
    {
        var d = (dept ?? string.Empty).Trim();
        var p = (prod ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(d) && string.IsNullOrWhiteSpace(p)) return string.Empty;
        return "-" + d + "-" + p + "-----";
    }

    private static string ToFscmDateLiteral(DateTime dtUtc)
    {
        var utc = DateTime.SpecifyKind(dtUtc, DateTimeKind.Utc);
        var ms = new DateTimeOffset(utc).ToUnixTimeMilliseconds();
        return $"/Date({ms})/";
    }
}
