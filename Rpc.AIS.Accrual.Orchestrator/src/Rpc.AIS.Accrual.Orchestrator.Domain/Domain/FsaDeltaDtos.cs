namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain;

/// <summary>
/// Carries fsa delta snapshot data.
/// </summary>
public sealed record FsaDeltaSnapshot(
    string WorkOrderNumber,
    Guid WorkOrderId,
    IReadOnlyList<FsaProductLine> InventoryProducts,
    IReadOnlyList<FsaProductLine> NonInventoryProducts,
    IReadOnlyList<FsaServiceLine> ServiceLines,
    WoHeaderMappingFields? Header = null);

/// <summary>
/// Carries fsa product line data.
/// </summary>
public sealed record FsaProductLine(
    Guid LineId,
    Guid WorkOrderId,
    string WorkOrderNumber,
    Guid? ProductId,
    string? ItemNumber,
    string ProductType, // Inventory | Non-Inventory | Unknown
    decimal? Quantity,
    decimal? UnitCost,
    decimal? FsaUnitPrice,          // msdyn_unitamount (unit price explicitly provided by FS)
    decimal? UnitAmount,
    string? Currency,
    string? Unit,
    string? JournalDescription,
    decimal? DiscountAmount,
    decimal? DiscountPercent,
    decimal? MarkupPercent,
    decimal? OverallDiscountAmount,
    decimal? OverallDiscountPercent,
    decimal? SurchargeAmount,
    decimal? SurchargePercent,
    decimal? MarkupAmount,
    string? CustomerProductReference,

    //  Delta enrichment
    decimal? CalculatedUnitPrice,     // rpc_calculatedunitprice
    string? LineProperty,             // msdyn_lineproperty (formatted or id)
    string? Department,               // rpc_department (formatted or id)
    string? ProductLine,              // rpc_productline (formatted or id)
    string? Warehouse,                // msdyn_warehouse (formatted or identifier later)
    string? Site,                     // msdyn_siteid resolved via warehouse -> operational site
    string? Location,                 // msdyn_location (formatted)
    bool? IsActive,                   // statecode -> true/false
    string? DataAreaId,               // msdyn_dataareaid
    bool? Printable,                // rpc_printable (Dataverse Yes/No)
    string? TaxabilityType,          // FSATaxabilityType / Taxability Type (line-level)
                                     // FSCM-derived project categories (NOT from Dataverse)
    string? ProjectCategory,       // Item/Expense journals
    DateTime? OperationsDateUtc
);

/// <summary>
/// Carries fsa service line data.
/// </summary>
public sealed record FsaServiceLine(
    Guid LineId,
    Guid WorkOrderId,
    string WorkOrderNumber,
    Guid? ProductId,
    decimal? Duration,
   decimal? UnitCost,
    decimal? FsaUnitPrice,          // msdyn_unitamount (unit price explicitly provided by FS)
    decimal? UnitAmount,
    string? Currency,
    string? Unit,
    string? JournalDescription,
    decimal? DiscountAmount,
    decimal? DiscountPercent,
    decimal? MarkupPercent,
    decimal? OverallDiscountAmount,
    decimal? OverallDiscountPercent,
    decimal? SurchargeAmount,
    decimal? SurchargePercent,
    decimal? MarkupAmount,
    string? CustomerProductReference,

    // Delta enrichment (same reversal triggers)
    decimal? CalculatedUnitPrice,     // rpc_calculatedunitprice
    string? LineProperty,             // msdyn_lineproperty
    string? Department,               // rpc_department
    string? ProductLine,              // rpc_productline
    bool? IsActive,                   // statecode
    string? DataAreaId,                // msdyn_dataareaid
    bool? Printable,
    string? TaxabilityType,          // FSATaxabilityType / Taxability Type (line-level)
    DateTime? OperationsDateUtc
);

/// <summary>
/// Carries fscm baseline record data.
/// </summary>
public sealed record FscmBaselineRecord(
    string WorkOrderNumber,
    string JournalType,
    string LineKey,
    string Hash);

/// <summary>
/// Carries delta comparison result data.
/// </summary>
public sealed record DeltaComparisonResult(
    string WorkOrderNumber,
    string JournalType,
    int AddedOrChanged,
    int Removed);

/// <summary>
/// Mapping-only work order header fields fetched from Dataverse and injected into outbound payload.
/// Not used by delta calculation.
/// </summary>
public sealed record WoHeaderMappingFields(
    DateTime? ActualStartDateUtc,
    DateTime? ActualEndDateUtc,
    DateTime? ProjectedStartDateUtc,
    DateTime? ProjectedEndDateUtc,
    decimal? WellLatitude,
    decimal? WellLongitude,
    string? InvoiceNotesInternal,
    string? InvoiceNotesExternal,
    string? PONumber,
    string? DeclinedToSignReason,
    string? Department,
    string? ProductLine,
    string? Warehouse,
    string? FSATaxabilityType,
    string? FSAWellAge,
    string? FSAWorkType,
    string? Country,
    string? County,
    string? State
);
