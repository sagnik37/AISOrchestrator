using System;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain;

/// <summary>
/// FSCM-sourced payload snapshot used to build REVERSAL delta lines.
/// For reversal lines only, AIS must rely on FSCM values (not FSA).
/// </summary>
public sealed record FscmReversalPayloadSnapshot
(
    // Identifiers / core
    Guid WorkOrderLineId,
    string? Currency,
    string? DimensionDisplayValue,
    decimal? FsaUnitPrice,
    string? ItemId,
    string? ProjectCategory,
    string? JournalLineDescription,
    string? LineProperty,
    decimal Quantity,

    // Optional unmapped by FSCM (kept for completeness)
    string? RcpCustomerProductReference,

    // Discounts / markups / surcharges
    decimal? RpcDiscountAmount,
    decimal? RpcDiscountPercent,
    decimal? RpcMarkupPercent,
    decimal? RpcOverallDiscountAmount,
    decimal? RpcOverallDiscountPercent,
    decimal? RpcSurchargeAmount,
    decimal? RpcSurchargePercent,
    decimal? RpMarkUpAmount,

    // Dates
    DateTime? TransactionDate,
    DateTime? OperationDate,

    // Amounts
    decimal? UnitAmount,
    decimal? UnitCost,

    // Printing / UOM / inventory dims
    bool? IsPrintable,
    string? UnitId,
    string? Warehouse,
    string? Site,

    // Descriptive fields
    string? FsaCustomerProductDesc,

    // Extra reversal-only headers
    string? FsaTaxabilityType
);
