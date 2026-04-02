using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services;

internal sealed partial class DeltaJournalSectionBuilder
{
    private sealed record ResolvedDeltaLine(
        Guid WorkOrderLineId,
        FsaWorkOrderLineSnapshot Fsa,
        decimal? FsaEffectiveUnitCost,
        decimal? FsaExplicitUnitPrice,
        bool UnitPriceProvided);

    private static ResolvedDeltaLine? ResolveDeltaLine(
        JsonObject lineObj,
        string qtyKey,
        JournalType jt,
        Guid woGuid,
        DateTime todayUtc,
        FscmWorkOrderLineAggregation? fscmAgg)
    {
        var lineId = GetWorkOrderLineGuid(lineObj);
        if (lineId == Guid.Empty) return null;

        var fsaQty = GetDecimalLoose(lineObj, qtyKey) ?? 0m;

        var fsaExplicitUnitPrice = GetDecimalLooseAny(lineObj, "FSAUnitPrice", "FsaUnitPrice");
        var unitPriceProvided = fsaExplicitUnitPrice.HasValue && fsaExplicitUnitPrice.Value != 0m;

        var fsaEffectiveUnitCost = GetDecimalLooseAny(lineObj, Keys.UnitAmount, "UnitAmount", "rpc_calculatedunitprice");
        var fsaUnitCost = fsaEffectiveUnitCost ?? (unitPriceProvided ? fsaExplicitUnitPrice : null);

        var linePropertyProvided = HasAnyNodeLoose(lineObj, Keys.LineProperty, "Line property", "LineProperty");
        var fsaLineProperty =
            GetStringLooseAny(lineObj, Keys.LineProperty, "Line property", "LineProperty");

        var custProdIdProvided = HasAnyNodeLoose(
            lineObj,
            "FSACustomerProductID",
            "rpc_customerproductid",
            "RPCCustomerProductReference");

        var fsaCustProdId = ResolveFsCustomerProductId(lineObj);

        var custProdDescProvided = HasAnyNodeLoose(
            lineObj,
            "FSACustomerProductDesc",
            "msdyn_description",
            "JournalLineDescription");

        var fsaCustProdDesc = ResolveFsDescription(lineObj);

        var taxabilityProvided = HasAnyNodeLoose(
            lineObj,
            "FSATaxabilityType",
            "Taxability Type");

        var fsaTaxability = ResolveFsTaxability(lineObj);

        var deptProvided = HasAnyNodeLoose(lineObj, Keys.DimDepartment, "Dimension department", "DimensionDepartment");
        var fsaDept =
            GetStringLooseAny(lineObj, Keys.DimDepartment, "Dimension department", "DimensionDepartment");

        var prodProvided = HasAnyNodeLoose(lineObj, Keys.DimProduct, "Dimension product", "DimensionProduct");
        var fsaProd =
            GetStringLooseAny(lineObj, Keys.DimProduct, "Dimension product", "DimensionProduct");

        if (!deptProvided || !prodProvided)
        {
            if (JsonLooseKey.TryGetStringLoose(lineObj, "DimensionDisplayValue", out var ddv) &&
                TryParseDeptProdFromDimensionDisplayValue(ddv, out var ddDept, out var ddProd))
            {
                if (!deptProvided)
                {
                    fsaDept = ddDept;
                    deptProvided = true;
                }

                if (!prodProvided)
                {
                    fsaProd = ddProd;
                    prodProvided = true;
                }
            }
        }

        var warehouseProvided = HasAnyNodeLoose(lineObj, Keys.Warehouse, "Warehouse");
        var fsaWarehouse = GetStringLooseAny(lineObj, Keys.Warehouse, "Warehouse");

        if (jt != JournalType.Item)
        {
            warehouseProvided = false;
            fsaWarehouse = null;
        }

        var opsDateUtc = ResolveOperationsDateUtc(lineObj, todayUtc);

        if (fscmAgg is not null && fscmAgg.DimensionBuckets is { Count: > 0 })
        {
            var b = fscmAgg.DimensionBuckets[0];

            if (!deptProvided) fsaDept = b.Department;
            if (!prodProvided) fsaProd = b.ProductLine;
            if (jt == JournalType.Item && !warehouseProvided)
                fsaWarehouse = b.Warehouse;
            if (!linePropertyProvided) fsaLineProperty = b.LineProperty;
            if (!custProdIdProvided) fsaCustProdId = b.CustomerProductId;
            if (!custProdDescProvided) fsaCustProdDesc = b.CustomerProductDescription;
            if (!taxabilityProvided) fsaTaxability = b.TaxabilityType;

            if (!unitPriceProvided)
                fsaUnitCost = b.CalculatedUnitPrice ?? fscmAgg.EffectiveUnitPrice;
            else
                fsaUnitCost = fsaEffectiveUnitCost ?? fsaExplicitUnitPrice;
        }

        return new ResolvedDeltaLine(
            WorkOrderLineId: lineId,
            Fsa: new FsaWorkOrderLineSnapshot(
                WorkOrderId: woGuid,
                WorkOrderLineId: lineId,
                JournalType: jt,
                IsActive: GetIsActive(lineObj),
                Quantity: fsaQty,
                CalculatedUnitPrice: fsaUnitCost,
                LineProperty: fsaLineProperty,
                Department: fsaDept,
                ProductLine: fsaProd,
                Warehouse: fsaWarehouse,
                CustomerProductId: fsaCustProdId,
                CustomerProductDescription: fsaCustProdDesc,
                TaxabilityType: fsaTaxability,
                OperationsDateUtc: opsDateUtc,
                DepartmentProvided: deptProvided,
                ProductLineProvided: prodProvided,
                WarehouseProvided: warehouseProvided,
                LinePropertyProvided: linePropertyProvided,
                UnitPriceProvided: unitPriceProvided,
                CustomerProductIdProvided: custProdIdProvided,
                CustomerProductDescriptionProvided: custProdDescProvided,
                TaxabilityTypeProvided: taxabilityProvided),
            FsaEffectiveUnitCost: fsaEffectiveUnitCost,
            FsaExplicitUnitPrice: fsaExplicitUnitPrice,
            UnitPriceProvided: unitPriceProvided);
    }
}
