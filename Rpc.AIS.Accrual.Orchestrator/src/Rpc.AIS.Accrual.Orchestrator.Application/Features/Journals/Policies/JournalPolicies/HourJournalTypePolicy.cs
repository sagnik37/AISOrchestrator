namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.JournalPolicies;

using System;
using System.Collections.Generic;
using System.Text.Json;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;

public sealed class HourJournalTypePolicy : JournalTypePolicyBase
{
    public override JournalType JournalType => JournalType.Hour;

    public override string SectionKey => "WOHourLines";

    protected override void ValidateLocalLineSpecific(
        Guid woGuid,
        string? woNumber,
        Guid lineGuid,
        JsonElement line,
        List<WoPayloadValidationFailure> invalidFailures)
    {
        // Required for Hour journals:
        // - Duration (Hours)
        // - LineProperty
        // - UnitCost/ProjectSalesPrice
        // - UnitId

        if (!WoPayloadJson.TryGetNumber(line, "Duration", out _))
        {
            invalidFailures.Add(new WoPayloadValidationFailure(
                woGuid,
                woNumber,
                JournalType,
                lineGuid,
                "AIS_HOUR_MISSING_DURATION",
                "Duration (Hours) is required for Hour journals.",
                ValidationDisposition.Invalid));
        }

        if (string.IsNullOrWhiteSpace(WoPayloadJson.TryGetString(line, "LineProperty")))
        {
            invalidFailures.Add(new WoPayloadValidationFailure(
                woGuid,
                woNumber,
                JournalType,
                lineGuid,
                "AIS_HOUR_MISSING_LINEPROPERTY",
                "LineProperty is required for Hour journals.",
                ValidationDisposition.Invalid));
        }

        if (!TryGetAnyNumber(line, out _, "UnitCost", "ProjectSalesPrice", "SalesPrice"))
        {
            invalidFailures.Add(new WoPayloadValidationFailure(
                woGuid,
                woNumber,
                JournalType,
                lineGuid,
                "AIS_HOUR_MISSING_SALESPRICE",
                "UnitCost/ProjectSalesPrice is required for Hour journals.",
                ValidationDisposition.Invalid));
        }

        if (string.IsNullOrWhiteSpace(WoPayloadJson.TryGetString(line, "UnitId")))
        {
            invalidFailures.Add(new WoPayloadValidationFailure(
                woGuid,
                woNumber,
                JournalType,
                lineGuid,
                "AIS_HOUR_MISSING_UNITID",
                "UnitId is required for Hour journals.",
                ValidationDisposition.Invalid));
        }
    }

    private static bool TryGetAnyNumber(JsonElement obj, out decimal value, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (WoPayloadJson.TryGetNumber(obj, k, out value))
                return true;
        }

        value = 0m;
        return false;
    }
}
