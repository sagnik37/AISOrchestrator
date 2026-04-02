using System;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain;

/// <summary>
/// Normalized FSCM journal line representation used by AIS delta logic.
/// The fetch client maps OData entity sets (Item/Expense/Hour) into this shape.
/// </summary>
public sealed record FscmJournalLine
(
    JournalType JournalType,

    // The Work Order and Work Order Line these journals correspond to
    Guid WorkOrderId,
    Guid WorkOrderLineId,

    // Subproject this journal line is posted to (required for Customer Change)
    string? SubProjectId,

    // Delta-significant numeric values
    decimal Quantity,
    decimal? CalculatedUnitPrice,
    decimal? ExtendedAmount,

    // Delta-significant dimensions/attributes (the ones that trigger reversal logic)
    string? Department,
    string? ProductLine,
    string? Warehouse,
    string? LineProperty,
    string? CustomerProductId,
    string? CustomerProductDescription,
    string? TaxabilityType,

    // Period/date controls for closed/open logic
    DateTime? TransactionDate,

    // Useful context
    string? DataAreaId,
    string? SourceJournalNumber,

    // Optional: only populated when fetch policy selects reversal mapping fields
    FscmReversalPayloadSnapshot? PayloadSnapshot = null
);

