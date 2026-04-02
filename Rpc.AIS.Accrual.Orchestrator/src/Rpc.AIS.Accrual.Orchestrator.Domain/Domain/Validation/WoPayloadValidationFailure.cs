using System;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

/// <summary>
/// Represents a single record detected during AIS-side validation.
/// </summary>
public sealed record WoPayloadValidationFailure(
    Guid WorkOrderGuid,
    string? WorkOrderNumber,
    JournalType JournalType,
    Guid? WorkOrderLineGuid,
    string Code,
    string Message,
    ValidationDisposition Disposition);
