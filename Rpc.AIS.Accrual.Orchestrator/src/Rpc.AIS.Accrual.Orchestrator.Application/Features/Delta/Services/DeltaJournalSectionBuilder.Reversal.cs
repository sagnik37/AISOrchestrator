using System;
using System.Globalization;
using System.Text.Json.Nodes;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services;

internal sealed partial class DeltaJournalSectionBuilder
{
    private static DateTime ResolveOperationsDateUtc(JsonObject lineObj, DateTime todayUtc)
    {
        if (JsonLooseKey.TryGetStringLoose(lineObj, "OperationDate", out var opLiteral) &&
            !string.IsNullOrWhiteSpace(opLiteral) &&
            TryParseFscmDateLiteral(opLiteral!, out var opUtc))
        {
            return opUtc.Date;
        }

        if (JsonLooseKey.TryGetStringLoose(lineObj, Keys.RpcWorkingDate, out var rpcWorkingLiteral) &&
            !string.IsNullOrWhiteSpace(rpcWorkingLiteral) &&
            TryParseFscmDateLiteral(rpcWorkingLiteral!, out var rpcWorkingUtc))
        {
            return rpcWorkingUtc.Date;
        }

        if (JsonLooseKey.TryGetStringLoose(lineObj, Keys.TransactionDate, out var txLiteral) &&
            !string.IsNullOrWhiteSpace(txLiteral) &&
            TryParseFscmDateLiteral(txLiteral!, out var txUtc))
        {
            return txUtc.Date;
        }

        if (JsonLooseKey.TryGetNodeLoose(lineObj, Keys.RpcOperationsDate, out var node) && node is not null)
        {
            var s = node.ToString();

            if (TryParseIsoUtc(s) is DateTime iso)
                return iso.Date;

            if (!string.IsNullOrWhiteSpace(s) && TryParseFscmDateLiteral(s, out var dt))
                return dt.Date;
        }

        return todayUtc.Date;
    }

    private static JsonObject BuildReversalLineFromFscmSnapshot(FscmReversalPayloadSnapshot snap, JournalType jt, decimal plannedQuantity)
    {
        var o = new JsonObject();

        o[Keys.WorkOrderLineGuid] = "{" + snap.WorkOrderLineId.ToString("D").ToUpperInvariant() + "}";

        if (!string.IsNullOrWhiteSpace(snap.Currency)) o["Currency"] = snap.Currency;
        if (!string.IsNullOrWhiteSpace(snap.DimensionDisplayValue)) o["DimensionDisplayValue"] = snap.DimensionDisplayValue;

        if (snap.FsaUnitPrice.HasValue) o["FSAUnitPrice"] = snap.FsaUnitPrice.Value;
        if (!string.IsNullOrWhiteSpace(snap.ItemId)) o["ItemId"] = snap.ItemId;
        if (!string.IsNullOrWhiteSpace(snap.ProjectCategory)) o["ProjectCategory"] = snap.ProjectCategory;
        if (!string.IsNullOrWhiteSpace(snap.JournalLineDescription)) o["JournalLineDescription"] = snap.JournalLineDescription;
        if (!string.IsNullOrWhiteSpace(snap.LineProperty)) o["LineProperty"] = snap.LineProperty;

        o[Keys.Quantity] = plannedQuantity;

        if (!string.IsNullOrWhiteSpace(snap.RcpCustomerProductReference))
        {
            o["FSACustomerProductID"] = snap.RcpCustomerProductReference;
            o["RPCCustomerProductReference"] = snap.RcpCustomerProductReference;
        }
        else
        {
            o["RPCCustomerProductReference"] = string.Empty;
        }

        if (snap.RpcDiscountAmount.HasValue) o["RPCDiscountAmount"] = snap.RpcDiscountAmount.Value;
        if (snap.RpcDiscountPercent.HasValue) o["RPCDiscountPercent"] = snap.RpcDiscountPercent.Value;
        if (snap.RpcMarkupPercent.HasValue) o["RPCMarkupPercent"] = snap.RpcMarkupPercent.Value;
        if (snap.RpcOverallDiscountAmount.HasValue) o["RPCOverallDiscountAmount"] = snap.RpcOverallDiscountAmount.Value;
        if (snap.RpcOverallDiscountPercent.HasValue) o["RPCOverallDiscountPercent"] = snap.RpcOverallDiscountPercent.Value;
        if (snap.RpcSurchargeAmount.HasValue) o["RPCSurchargeAmount"] = snap.RpcSurchargeAmount.Value;
        if (snap.RpcSurchargePercent.HasValue) o["RPCSurchargePercent"] = snap.RpcSurchargePercent.Value;
        if (snap.RpMarkUpAmount.HasValue) o["RPMarkUpAmount"] = snap.RpMarkUpAmount.Value;

        if (snap.OperationDate is DateTime opDate)
        {
            o["OperationDate"] = ToFscmDateLiteral(opDate.Date);
        }

        if (snap.TransactionDate is DateTime txDate)
        {
            o["TransactionDate"] = ToFscmDateLiteral(txDate.Date);
        }

        if (snap.UnitAmount.HasValue) o[Keys.UnitAmount] = snap.UnitAmount.Value;

        if (snap.IsPrintable.HasValue) o["IsPrintable"] = snap.IsPrintable.Value;
        if (!string.IsNullOrWhiteSpace(snap.UnitId)) o["UnitId"] = snap.UnitId;

        if (!string.IsNullOrWhiteSpace(snap.Warehouse)) o["Warehouse"] = snap.Warehouse;
        if (!string.IsNullOrWhiteSpace(snap.Site)) o["Site"] = snap.Site;

        if (!string.IsNullOrWhiteSpace(snap.FsaCustomerProductDesc)) o["FSACustomerProductDesc"] = snap.FsaCustomerProductDesc;

        if (!string.IsNullOrWhiteSpace(snap.FsaTaxabilityType)) o["FSATaxabilityType"] = snap.FsaTaxabilityType;

        return o;
    }

    static void ComputeAndSetUnitAmount(JsonObject lineObj, string qtyKey)
    {
        var existing = GetDecimalLooseAny(lineObj, Keys.UnitAmount, "UnitAmount");
        if (existing.HasValue) return;

        var fsaUnitPrice = GetDecimalLooseAny(lineObj, "FSAUnitPrice", "FsaUnitPrice");
        if (fsaUnitPrice.HasValue)
            SetOrAddLoose(lineObj, Keys.UnitAmount, fsaUnitPrice.Value);
    }

    private static bool TryParseDeptProdFromDimensionDisplayValue(string? ddv, out string? dept, out string? prod)
    {
        dept = null;
        prod = null;

        if (string.IsNullOrWhiteSpace(ddv))
            return false;

        var s = ddv.Trim();

        if (!s.StartsWith("-", StringComparison.Ordinal))
            return false;

        var parts = s.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;

        dept = parts[0].Trim();
        prod = parts[1].Trim();

        return !(string.IsNullOrWhiteSpace(dept) || string.IsNullOrWhiteSpace(prod));
    }

    private static string BuildDefaultDimensionDisplayValue(string? dept, string? prod)
    {
        var d = (dept ?? string.Empty).Trim();
        var p = (prod ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(d) && string.IsNullOrWhiteSpace(p)) return string.Empty;
        return "-" + d + "-" + p + "-----";
    }
}
