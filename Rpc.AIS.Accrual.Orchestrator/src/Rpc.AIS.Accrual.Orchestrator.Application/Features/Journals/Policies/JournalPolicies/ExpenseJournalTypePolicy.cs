namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.JournalPolicies;

using System;
using System.Collections.Generic;
using System.Text.Json;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;

public sealed class ExpenseJournalTypePolicy : JournalTypePolicyBase
{
    public override JournalType JournalType => JournalType.Expense;

    public override string SectionKey => "WOExpLines";

    protected override void ValidateLocalLineSpecific(
        Guid woGuid,
        string? woNumber,
        Guid lineGuid,
        JsonElement line,
        List<WoPayloadValidationFailure> invalidFailures)
    {
        // Required for Expense journals (FSCM ExpenseJournalLineEntity):
        // - ProjectCategory
        // - LineProperty (ProjectLinePropertyId)
        // - UnitCost/ProjectSalesPrice
        // - UnitId

        if (string.IsNullOrWhiteSpace(WoPayloadJson.TryGetString(line, "ProjectCategory")))
        {
            invalidFailures.Add(new WoPayloadValidationFailure(
                woGuid,
                woNumber,
                JournalType,
                lineGuid,
                "AIS_EXP_MISSING_RPCPROJCATEGORYID",
                "ProjectCategory is required for Expense journals.",
                ValidationDisposition.Invalid));
        }

        if (string.IsNullOrWhiteSpace(WoPayloadJson.TryGetString(line, "LineProperty")))
        {
            invalidFailures.Add(new WoPayloadValidationFailure(
                woGuid,
                woNumber,
                JournalType,
                lineGuid,
                "AIS_EXP_MISSING_LINEPROPERTY",
                "LineProperty is required for Expense journals.",
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
