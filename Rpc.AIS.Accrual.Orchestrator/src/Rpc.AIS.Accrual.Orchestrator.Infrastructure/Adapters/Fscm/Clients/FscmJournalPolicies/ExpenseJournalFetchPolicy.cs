namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.FscmJournalPolicies;

using System;
using System.Text.Json;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

public sealed class ExpenseJournalFetchPolicy : FscmJournalFetchPolicyBase
{
    private const string WorkOrderIdField = "RPCWorkOrderGuid";
    private const string WorkOrderLineIdField = "RPCWorkOrderLineGuid";

    public override JournalType JournalType => JournalType.Expense;
    public override string EntitySet => "ExpenseJournalLines";

    /// <summary>
    /// $select list for ExpenseJournalLines.
    /// Avoid 'LineAmount' unless  have confirmed it exists in  FSCM metadata.
    /// Keep 'Amount' as optional extended amount when exposed.
    /// </summary>
    public override string Select => string.Join(",",
       WorkOrderIdField,
        WorkOrderLineIdField,
        //"ProjectId",
        "ProjectSalesCurrencyCode",
        "ProjectCategoryId",
        "ProjectCategory",
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
        "DimensionDisplayValue",
        "ProjectDate",
        "ProjectUnitID",
        "StorageWarehouseId",
        "StorageSiteId"
    );

    /// <summary>
    /// Since we are not requesting any known-problematic fields, fallback can be identical.
    /// </summary>
    public override string SelectFallback => Select
        .Replace(",ProjectCategory", string.Empty)
        .Replace(",StorageWarehouseId", string.Empty)
        .Replace(",StorageSiteId", string.Empty)
        .Replace(",RPCFSAMarkupAmt", string.Empty);

    public override decimal GetQuantity(JsonElement row) =>
        TryGetDecimal(row, "Quantity")
        ?? TryGetDecimal(row, "Qty")
        ?? 0m;

    /// <summary>
    /// Returns a unit price suitable for delta comparisons.
    /// Uses the same normalization approach as Item: if ProjectSalesPrice looks extended and Amount exists, normalize.
    /// </summary>
    public override decimal? GetUnitPrice(JsonElement row)
    {
        var qty = GetQuantity(row);

        var salesPrice = TryGetDecimal(row, "ProjectSalesPrice")
                         ?? TryGetDecimal(row, "SalesPrice");

        if (!salesPrice.HasValue)
            return null;

        var amount = TryGetDecimal(row, "Amount");

        if (qty != 0m && amount.HasValue)
        {
            if (NearlyEqual(salesPrice.Value, amount.Value))
                return amount.Value / qty;

            if (NearlyEqual(amount.Value, qty * salesPrice.Value))
                return salesPrice.Value;

            if (NearlyEqual(amount.Value / qty, salesPrice.Value))
                return salesPrice.Value;
        }

        return salesPrice.Value;
    }

    private static bool NearlyEqual(decimal a, decimal b)
        => Math.Abs(a - b) <= AisConstants.Delta.PriceComparisonTolerance;
}
