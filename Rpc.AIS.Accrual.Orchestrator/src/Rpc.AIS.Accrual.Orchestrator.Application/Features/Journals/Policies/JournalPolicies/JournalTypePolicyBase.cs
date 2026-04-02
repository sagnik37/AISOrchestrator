namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.JournalPolicies;

using System;
using System.Collections.Generic;
using System.Text.Json;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;

/// <summary>
/// Base journal policy with shared validations (quantity required for all).
/// </summary>
public abstract class JournalTypePolicyBase : IJournalTypePolicy
{
    public abstract JournalType JournalType { get; }

    public abstract string SectionKey { get; }

    public void ValidateLocalLine(
        Guid woGuid,
        string? woNumber,
        Guid lineGuid,
        JsonElement line,
        List<WoPayloadValidationFailure> invalidFailures)
    {
        // Quantity is required for all journal types.
        if (!WoPayloadJson.TryGetNumber(line, "Quantity", out _))
        {
            invalidFailures.Add(new WoPayloadValidationFailure(
                woGuid,
                woNumber,
                JournalType,
                lineGuid,
                "AIS_LINE_MISSING_QUANTITY",
                "Quantity is missing or not numeric.",
                ValidationDisposition.Invalid));
        }

        ValidateLocalLineSpecific(woGuid, woNumber, lineGuid, line, invalidFailures);
    }

    protected abstract void ValidateLocalLineSpecific(
        Guid woGuid,
        string? woNumber,
        Guid lineGuid,
        JsonElement line,
        List<WoPayloadValidationFailure> invalidFailures);
}
