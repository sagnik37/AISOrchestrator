using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.FscmJournalPolicies;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

public sealed class FscmJournalLineRowMapper : IFscmJournalLineRowMapper
{
    private readonly ILogger<FscmJournalLineRowMapper> _logger;

    public FscmJournalLineRowMapper(ILogger<FscmJournalLineRowMapper> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<FscmJournalLine> MapMany(string json, IFscmJournalFetchPolicy policy, string workOrderLineIdField)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<FscmJournalLine>();

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
            return Array.Empty<FscmJournalLine>();

        var result = new List<FscmJournalLine>(value.GetArrayLength());
        foreach (var row in value.EnumerateArray())
        {
            var mapped = MapSingle(row, policy, workOrderLineIdField);
            if (mapped is not null)
                result.Add(mapped);
        }

        return result;
    }

    private FscmJournalLine? MapSingle(JsonElement row, IFscmJournalFetchPolicy policy, string workOrderLineIdField)
    {
        var woId = TryGetGuidLoose(row, "RPCWorkOrderGuid") ?? TryGetGuidLoose(row, "WorkOrderGuid") ?? Guid.Empty;
        if (woId == Guid.Empty) return null;

        var woLineId =
            TryGetGuid(row, workOrderLineIdField) ??
            TryGetGuid(row, "WorkOrderLineId") ??
            TryGetGuid(row, "WorkOrderLine") ??
            Guid.Empty;
        if (woLineId == Guid.Empty) return null;

        var quantity = policy.GetQuantity(row);
        var unitPrice = policy.GetUnitPrice(row);

        var lineProperty =
            TryGetString(row, "LineProperty") ??
            TryGetString(row, "ProjectLinePropertyId") ??
            TryGetString(row, "ProjectLineProperty") ??
            TryGetNumberAsString(row, "ProjectLinePropertyId") ??
            TryGetNumberAsString(row, "ProjectLineProperty");

        var dimKey = policy.JournalType == JournalType.Item
            ? "DefaultDimensionDisplayValue"
            : "DimensionDisplayValue";

        var dimDisplay = TryGetString(row, dimKey);
        var (dept, productLine) = ParseDefaultDimensionDisplayValue(dimDisplay);
        if (dept is null || productLine is null)
        {
            _logger.LogWarning(
                "DimensionDisplayValue missing/invalid. DimensionField={DimensionField} JournalType={JournalType} WorkOrderId={WorkOrderId} WorkOrderLineId={WorkOrderLineId} Value='{Value}'",
                dimKey,
                policy.JournalType,
                woId,
                woLineId,
                dimDisplay);
        }

        var transDate =
            TryGetDate(row, "ProjectDate") ??
            TryGetDate(row, "VoucherDate") ??
            TryGetDate(row, "TransDate") ??
            TryGetDate(row, "PostingDate");

        var subProjectId = TryGetString(row, "SubProjectId") ?? TryGetString(row, "ProjId") ?? TryGetString(row, "ProjectId");
        var dataArea = TryGetString(row, "DataAreaId");
        var journalNum = TryGetString(row, "JournalNum");

        var extAmount = TryGetDecimal(row, "LineAmount") ?? TryGetDecimal(row, "Amount");
        if (!extAmount.HasValue && unitPrice.HasValue)
            extAmount = quantity * unitPrice.Value;

        var custProdId =
            TryGetString(row, "RPCFSACustProdId") ??
            TryGetString(row, "RPCFSACustomerProductId") ??
            TryGetString(row, "RPCCustomerProductReference") ??
            TryGetString(row, "FSACustomerProductID");

        var custProdDesc = TryGetString(row, "RPCFSACustProdDesc");
        var taxabilityType = TryGetString(row, "RPCFSATaxabilityType");
        var snapshot = BuildPayloadSnapshot(row, policy, woLineId, quantity);

        return new FscmJournalLine(
            JournalType: policy.JournalType,
            WorkOrderId: woId,
            WorkOrderLineId: woLineId,
            SubProjectId: subProjectId,
            Quantity: quantity,
            CalculatedUnitPrice: unitPrice,
            ExtendedAmount: extAmount,
            Department: dept,
            ProductLine: productLine,
            Warehouse: TryGetString(row, "Warehouse") ?? TryGetString(row, "StorageWarehouseId"),
            LineProperty: lineProperty,
            CustomerProductId: custProdId,
            CustomerProductDescription: custProdDesc,
            TaxabilityType: taxabilityType,
            TransactionDate: transDate,
            DataAreaId: dataArea,
            SourceJournalNumber: journalNum,
            PayloadSnapshot: snapshot);
    }

    private static FscmReversalPayloadSnapshot? BuildPayloadSnapshot(JsonElement row, IFscmJournalFetchPolicy policy, Guid woLineId, decimal quantity)
    {
        try
        {
            var currency = policy.JournalType == JournalType.Item
                ? TryGetString(row, "ProjectSalesCurrencyId") ?? TryGetString(row, "ProjectSalesCurrencyId")
                : TryGetString(row, "ProjectSalesCurrencyCode") ?? TryGetString(row, "ProjectSalesCurrencyCode");

            var dim = policy.JournalType == JournalType.Item
                ? TryGetString(row, "DefaultDimensionDisplayValue") ?? TryGetString(row, "DimensionDisplayValue")
                : TryGetString(row, "DimensionDisplayValue") ?? TryGetString(row, "DefaultDimensionDisplayValue");

            var fsaUnitPrice = TryGetDecimal(row, "RPCFSAUnitPrice");
            var itemId = TryGetString(row, "ItemId");
            var projectCategory = TryGetString(row, "ProjectCategory") ?? TryGetString(row, "ProjectCategoryId");
            var custProdDesc = TryGetString(row, "RPCFSACustProdDesc");
            var custProdId =
                TryGetString(row, "RPCFSACustProdId") ??
                TryGetString(row, "RPCFSACustomerProductId") ??
                TryGetString(row, "RPCCustomerProductReference") ??
                TryGetString(row, "FSACustomerProductID");
            var lineProperty = TryGetString(row, "ProjectLinePropertyId");

            var rpcDiscountAmt = TryGetDecimal(row, "RPCFSADiscountAmt");
            var rpcDiscountPrct = TryGetDecimal(row, "RPCFSADiscountPrct");
            var rpcMarkupPrct = TryGetDecimal(row, "RPCFSAMarkupPrct");
            var rpcMarkupAmt = TryGetDecimal(row, "RPCFSAMarkupAmt");
            var rpcOverallDiscAmt = TryGetDecimal(row, "RPCFSAOverallDiscAmt");
            var rpcOverallDiscPrct = TryGetDecimal(row, "RPCFSAOverallDiscPrct");
            var rpcSurchargeAmt = TryGetDecimal(row, "RPCFSASurchargeAmt");
            var rpcSurchargePrct = TryGetDecimal(row, "RPCFSASurchargePrct");

            var projectDate = TryGetDate(row, "ProjectDate");
            var opDate = TryGetDate(row, "RPCOperationDate");
            var projectSalesPrice = TryGetDecimal(row, "ProjectSalesPrice");
            var isPrintable = TryGetBool(row, "RPCFSAIsPrintable");
            var unitId = TryGetString(row, "ProjectUnitID");
            var wh = TryGetString(row, "StorageWarehouseId") ?? TryGetString(row, "Warehouse");
            var site = TryGetString(row, "StorageSiteId");
            var taxabilityType = TryGetString(row, "RPCFSATaxabilityType");

            if (currency is null && dim is null && !fsaUnitPrice.HasValue && itemId is null && projectCategory is null &&
                custProdDesc is null && custProdId is null && lineProperty is null && !projectSalesPrice.HasValue &&
                !projectDate.HasValue && !opDate.HasValue)
            {
                return null;
            }

            return new FscmReversalPayloadSnapshot(
                WorkOrderLineId: woLineId,
                Currency: currency,
                DimensionDisplayValue: dim,
                FsaUnitPrice: fsaUnitPrice,
                ItemId: itemId,
                ProjectCategory: projectCategory,
                JournalLineDescription: custProdDesc,
                LineProperty: lineProperty,
                Quantity: quantity,
                RcpCustomerProductReference: custProdId,
                RpcDiscountAmount: rpcDiscountAmt,
                RpcDiscountPercent: rpcDiscountPrct,
                RpcMarkupPercent: rpcMarkupPrct,
                RpcOverallDiscountAmount: rpcOverallDiscAmt,
                RpcOverallDiscountPercent: rpcOverallDiscPrct,
                RpcSurchargeAmount: rpcSurchargeAmt,
                RpcSurchargePercent: rpcSurchargePrct,
                RpMarkUpAmount: rpcMarkupAmt,
                TransactionDate: projectDate,
                OperationDate: opDate,
                UnitAmount: projectSalesPrice,
                UnitCost: fsaUnitPrice,
                IsPrintable: isPrintable,
                UnitId: unitId,
                Warehouse: wh,
                Site: site,
                FsaCustomerProductDesc: custProdDesc,
                FsaTaxabilityType: taxabilityType);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetNumberAsString(JsonElement row, string prop)
    {
        if (!row.TryGetProperty(prop, out var p)) return null;
        return p.ValueKind == JsonValueKind.Number ? p.ToString() : null;
    }

    private static (string? Department, string? ProductLine) ParseDefaultDimensionDisplayValue(string? displayValue)
    {
        if (string.IsNullOrWhiteSpace(displayValue))
            return (null, null);

        var parts = displayValue.Split('-', StringSplitOptions.None);
        if (parts.Length < 3)
            return (null, null);

        static string? Normalize(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        return (Normalize(parts[1]), Normalize(parts[2]));
    }

    private static Guid? TryGetGuidLoose(JsonElement obj, string propName)
    {
        if (!obj.TryGetProperty(propName, out var p))
            return null;
        if (p.ValueKind == JsonValueKind.String && Guid.TryParse(p.GetString(), out var g))
            return g;
        var t = p.ToString();
        return Guid.TryParse(t, out var g2) ? g2 : null;
    }

    private static Guid? TryGetGuid(JsonElement obj, string propName)
    {
        if (!obj.TryGetProperty(propName, out var p))
            return null;
        if (p.ValueKind == JsonValueKind.String && Guid.TryParse(p.GetString(), out var g))
            return g;
        return null;
    }

    private static decimal? TryGetDecimal(JsonElement obj, string propName)
    {
        if (!obj.TryGetProperty(propName, out var p))
            return null;
        return p.ValueKind switch
        {
            JsonValueKind.Number when p.TryGetDecimal(out var d) => d,
            JsonValueKind.String => decimal.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null,
            _ => null
        };
    }

    private static DateTime? TryGetDate(JsonElement obj, string propName)
    {
        if (!obj.TryGetProperty(propName, out var p))
            return null;
        if (p.ValueKind == JsonValueKind.String && DateTime.TryParse(p.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
            return dt;
        return null;
    }

    private static string? TryGetString(JsonElement obj, string propName)
    {
        if (!obj.TryGetProperty(propName, out var p))
            return null;
        return p.ValueKind switch
        {
            JsonValueKind.String => p.GetString(),
            JsonValueKind.Number => p.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static bool? TryGetBool(JsonElement obj, string propName)
    {
        if (!obj.TryGetProperty(propName, out var p))
            return null;
        return p.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => ParseBoolLoose(p.GetString()),
            JsonValueKind.Number => p.TryGetInt32(out var i) ? i != 0 : (bool?)null,
            _ => null
        };

        static bool? ParseBoolLoose(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (bool.TryParse(s, out var b)) return b;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i != 0;
            return null;
        }
    }
}
