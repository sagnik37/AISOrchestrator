namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.JournalPolicies;

using System;
using System.Collections.Generic;
using System.Text.Json;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;

public sealed class ItemJournalTypePolicy : JournalTypePolicyBase
{
    public override JournalType JournalType => JournalType.Item;

    public override string SectionKey => "WOItemLines";

    protected override void ValidateLocalLineSpecific(
        Guid woGuid,
        string? woNumber,
        Guid lineGuid,
        JsonElement line,
        List<WoPayloadValidationFailure> invalidFailures)
    {
        // Required for Item journals (FSCM ProjectItemJournalTransEntity):
        // - ItemId
        // - LineProperty (ProjectLinePropertyId)
        // - Warehouse (StorageWarehouseId)
        // - UnitCost/ProjectSalesPrice
        // - UnitId (ProjectUnitId)

        if (string.IsNullOrWhiteSpace(WoPayloadJson.TryGetString(line, "ItemId")))
        {
            invalidFailures.Add(new WoPayloadValidationFailure(
                woGuid,
                woNumber,
                JournalType,
                lineGuid,
                "AIS_ITEM_MISSING_ITEMID",
                "ItemId is required for Item journals.",
                ValidationDisposition.Invalid));
        }

        if (string.IsNullOrWhiteSpace(WoPayloadJson.TryGetString(line, "LineProperty")))
        {
            invalidFailures.Add(new WoPayloadValidationFailure(
                woGuid,
                woNumber,
                JournalType,
                lineGuid,
                "AIS_ITEM_MISSING_LINEPROPERTY",
                "LineProperty is required for Item journals.",
                ValidationDisposition.Invalid));
        }

        if (string.IsNullOrWhiteSpace(WoPayloadJson.TryGetString(line, "Warehouse")))
        {
            invalidFailures.Add(new WoPayloadValidationFailure(
                woGuid,
                woNumber,
                JournalType,
                lineGuid,
                "AIS_ITEM_MISSING_WAREHOUSE",
                "Warehouse is required for Item journals.",
                ValidationDisposition.Invalid));
        }


        if (string.IsNullOrWhiteSpace(WoPayloadJson.TryGetString(line, "UnitId")))
        {
            invalidFailures.Add(new WoPayloadValidationFailure(
                woGuid,
                woNumber,
                JournalType,
                lineGuid,
                "AIS_ITEM_MISSING_UNITID",
                "UnitId is required for Item journals.",
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
