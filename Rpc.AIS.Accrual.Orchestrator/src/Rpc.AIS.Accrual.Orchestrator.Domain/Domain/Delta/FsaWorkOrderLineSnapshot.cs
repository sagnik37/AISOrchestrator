using System;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;

/// <summary>
/// Canonical snapshot of the FSA WorkOrder line for delta evaluation.
/// Only includes fields relevant to delta + reversal logic.
/// </summary>
public sealed record FsaWorkOrderLineSnapshot
(
    Guid WorkOrderId,
    Guid WorkOrderLineId,
    JournalType JournalType,
    bool IsActive,

    // Delta-significant values
    decimal Quantity,
    decimal? CalculatedUnitPrice,
    string? LineProperty,
    string? Department,
    string? ProductLine,
    string? Warehouse,
    string? CustomerProductId,
    string? CustomerProductDescription,
    string? TaxabilityType,
    DateTime? OperationsDateUtc,

    // Presence flags (distinguish 'missing in payload' vs explicit null)
    bool DepartmentProvided,
    bool ProductLineProvided,
    bool WarehouseProvided,
    bool LinePropertyProvided,
    bool UnitPriceProvided,
    bool CustomerProductIdProvided,
    bool CustomerProductDescriptionProvided,
    bool TaxabilityTypeProvided
);
