namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.FscmJournalPolicies;

using System;
using System.Text.Json;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

public sealed class ItemJournalFetchPolicy : FscmJournalFetchPolicyBase
{
    private const string WorkOrderIdField = "RPCWorkOrderGuid";
    private const string WorkOrderLineIdField = "RPCWorkOrderLineGuid";

    public override JournalType JournalType => JournalType.Item;
    public override string EntitySet => "ProjectItemJournalTrans";

    /// <summary>
    /// $select list for ProjectItemJournalTrans.
    /// : 'LineAmount' is NOT available in  environment (confirmed by OData error),
    /// so we only include 'Amount' as an optional extended amount if exposed by metadata.
    /// </summary>
    public override string Select => string.Join(",",
        WorkOrderIdField,
        WorkOrderLineIdField,
        "ProjectId",
        "ProjectSalesCurrencyId",
        "ProjectCategoryId",
        "Quantity",
        "ProjectSalesPrice",
       "RPCFSASurchargeAmt",
       "RPCFSADiscountPrct",
       "RPCFSAOverallDiscPrct",
       "RPCFSAOverallDiscAmt",
       "RPCFSATaxabilityType",
       "RPCFSAUnitPrice",
       "RPCFSASurchargePrct",
       "RPCFSACustProdDesc",
       "RPCFSACustProdId",
        "ItemId",
       "RPCOperationDate",
       "RPCFSAIsPrintable",
       "RPCFSADiscountAmt",
       "RPCFSAMarkupPrct",
       "RPCFSAMarkupAmt",
       "RPCFSAIsPrintable",
       "RPCFSADiscountAmt",
        "ProjectLinePropertyId",
        "DefaultDimensionDisplayValue",
        "ProjectDate",
        "StorageWarehouseId",
        "ProjectUnitID",
        "StorageSiteId"
    );

    /// <summary>
    /// Since we are not requesting any known-problematic fields, fallback can be identical.
    /// </summary>
    public override string SelectFallback => Select.Replace(",RPCFSAMarkupAmt", string.Empty);

    public override decimal GetQuantity(JsonElement row) =>
        TryGetDecimal(row, "Quantity")
        ?? TryGetDecimal(row, "Qty")
        ?? 0m;

    /// <summary>
    /// Returns a unit price suitable for delta comparisons.
    /// In some FSCM environments, ProjectSalesPrice can be an extended amount (line total) rather than a unit price.
    /// If 'Amount' is available and indicates ProjectSalesPrice is extended, normalize: unit = Amount / Quantity.
    /// If 'Amount' is missing, return ProjectSalesPrice as-is (best-effort).
    /// </summary>
    public override decimal? GetUnitPrice(JsonElement row)
    {
        var qty = GetQuantity(row);

        var salesPrice = TryGetDecimal(row, "ProjectSalesPrice")
                         ?? TryGetDecimal(row, "SalesPrice");

        if (!salesPrice.HasValue)
            return null;

        var amount = TryGetDecimal(row, "Amount");

        // If we have a reliable extended amount and a non-zero quantity, we can normalize.
        if (qty != 0m && amount.HasValue)
        {
            // If ProjectSalesPrice equals Amount, treat as extended -> normalize to unit.
            if (NearlyEqual(salesPrice.Value, amount.Value))
                return amount.Value / qty;

            // If Amount equals qty * salesPrice, then salesPrice is already unit.
            if (NearlyEqual(amount.Value, qty * salesPrice.Value))
                return salesPrice.Value;

            // If Amount/qty equals salesPrice, salesPrice is unit.
            if (NearlyEqual(amount.Value / qty, salesPrice.Value))
                return salesPrice.Value;
        }

        // Best-effort: treat ProjectSalesPrice as unit if we can't infer otherwise.
        return salesPrice.Value;
    }

    private static bool NearlyEqual(decimal a, decimal b)
        => Math.Abs(a - b) <= AisConstants.Delta.PriceComparisonTolerance;
}
