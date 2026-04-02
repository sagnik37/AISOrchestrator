using System;
using System.Text.Json;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.Mappers;

public sealed class FsaServiceLineMapper : IFsaServiceLineMapper
{
    public FsaServiceLine Map(JsonElement row, Guid workOrderId, string workOrderNumber)
    {
        if (!FsaDeltaPayloadJsonHelpers.TryGuid(row, "msdyn_workorderserviceid", out var lineId))
            throw new InvalidOperationException("Missing msdyn_workorderserviceid.");

        Guid? prodId =
            (FsaDeltaPayloadJsonHelpers.TryGuid(row, "_msdyn_service_value", out var pid1) ? pid1 : (Guid?)null)
            ?? (FsaDeltaPayloadJsonHelpers.TryGuid(row, "_msdyn_product_value", out var pid2) ? pid2 : (Guid?)null)
            ?? (FsaDeltaPayloadJsonHelpers.TryGuid(row, "_productid_value", out var pid3) ? pid3 : (Guid?)null);

        var calcPrice =
            FsaDeltaPayloadJsonHelpers.TryDecimal(row, "rpc_calculatedunitprice")
            ?? FsaDeltaPayloadJsonHelpers.TryDecimal(row, "msdyn_unitcost");
        var fsaUnitPrice = FsaDeltaPayloadJsonHelpers.TryDecimal(row, "msdyn_unitamount");

        var taxabilityType =
            (row.TryGetProperty("FSATaxabilityType", out var tt1) && tt1.ValueKind == JsonValueKind.String) ? tt1.GetString()
            : (row.TryGetProperty("Taxability Type", out var tt2) && tt2.ValueKind == JsonValueKind.String) ? tt2.GetString()
            : null;

        taxabilityType ??= FsaDeltaPayloadJsonHelpers.GetFormattedOnly(row, "_rpc_taxabilitytype_value");

        return new FsaServiceLine(
    LineId: lineId,
    WorkOrderId: workOrderId,
    WorkOrderNumber: workOrderNumber,
    ProductId: prodId,
    Duration: FsaDeltaPayloadJsonHelpers.TryDecimal(row, "msdyn_duration"),
    UnitCost: null,
    FsaUnitPrice: fsaUnitPrice,
    UnitAmount: calcPrice ?? fsaUnitPrice,
    Currency: FsaDeltaPayloadJsonHelpers.TryCurrency(row),
    Unit: FsaDeltaPayloadJsonHelpers.TryLookupFormattedPreferred(row, "_msdyn_unit_value"),
    JournalDescription: FsaDeltaPayloadJsonHelpers.TryJournalDescription(row),

    DiscountAmount: FsaDeltaPayloadJsonHelpers.TryDecimal(row, "rpc_lineitemdiscountamount"),
    DiscountPercent: FsaDeltaPayloadJsonHelpers.TryDecimal(row, "rpc_lineitemdiscount"),
    MarkupPercent: FsaDeltaPayloadJsonHelpers.TryDecimal(row, "rpc_lineitemmarkup"),
    OverallDiscountAmount: FsaDeltaPayloadJsonHelpers.TryDecimal(row, "rpc_applyoveralldiscountamount"),
    OverallDiscountPercent: FsaDeltaPayloadJsonHelpers.TryDecimal(row, "rpc_applyoveralldiscount"),
    SurchargeAmount: FsaDeltaPayloadJsonHelpers.TryDecimal(row, "rpc_surchargeamount"),
    SurchargePercent: FsaDeltaPayloadJsonHelpers.TryDecimalAny(row, "rpc_surcharge", "rpc_surchage"),
    MarkupAmount: FsaDeltaPayloadJsonHelpers.TryDecimal(row, "rpc_lineitemmarkupamount"),

    CustomerProductReference: row.TryGetProperty("rpc_customerproductid", out var cpr) && cpr.ValueKind == JsonValueKind.String
        ? cpr.GetString()
        : null,

    CalculatedUnitPrice: calcPrice,
    LineProperty: FsaDeltaPayloadJsonHelpers.GetFormattedOnly(row, "rpc_lineproperties"),
    Department: FsaDeltaPayloadJsonHelpers.GetFormattedOnly(row, "_rpc_departments_value"),
    ProductLine: FsaDeltaPayloadJsonHelpers.GetFormattedOnly(row, "_rpc_productlines_value"),
    IsActive: FsaDeltaPayloadJsonHelpers.TryStatecodeActive(row),
    DataAreaId: row.TryGetProperty("msdyn_dataareaid", out var da) && da.ValueKind == JsonValueKind.String
        ? da.GetString()
        : null,
    Printable: FsaDeltaPayloadJsonHelpers.TryBool(row, "rpc_printable"),
    TaxabilityType: taxabilityType,
    OperationsDateUtc: FsaDeltaPayloadJsonHelpers.TryDateTimeUtc(row, "rpc_operationsdate")
);
    }
}
