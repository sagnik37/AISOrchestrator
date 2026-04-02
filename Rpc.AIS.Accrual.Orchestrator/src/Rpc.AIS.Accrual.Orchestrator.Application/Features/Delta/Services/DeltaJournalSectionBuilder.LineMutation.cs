using System;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services;

internal sealed partial class DeltaJournalSectionBuilder
{
    private async Task<JsonObject> CreatePlannedLineAsync(
        RunContext context,
        JsonObject sourceLine,
        ResolvedDeltaLine resolved,
        DeltaPlannedLine planned,
        JournalType jt,
        Guid woGuid,
        string? woNumber,
        FscmWorkOrderLineAggregation? fscmAgg,
        AccountingPeriodSnapshot period,
        DateTime todayUtc,
        string qtyKey,
        CancellationToken ct)
    {
        JsonObject cloned;

        var reversalSnapshot = planned.ReversalPayloadSnapshotOverride ?? fscmAgg?.RepresentativeSnapshot;
        if (planned.IsReversal
            && (jt == JournalType.Item || jt == JournalType.Expense)
            && reversalSnapshot is not null)
        {
            cloned = BuildReversalLineFromFscmSnapshot(
                snap: reversalSnapshot,
                jt: jt,
                plannedQuantity: planned.Quantity);
        }
        else
        {
            cloned = (JsonObject)sourceLine.DeepClone();
            var workingDate = resolved.Fsa.OperationsDateUtc?.Date ?? todayUtc.Date;
            var isClosedP = await period
                .IsDateInClosedPeriodAsync(workingDate, ct)
                .ConfigureAwait(false);

            var txDate = isClosedP
                ? period.CurrentOpenPeriodStartDate.Date
                : workingDate;

            cloned["OperationDate"] = ToFscmDateLiteral(workingDate);
            cloned["TransactionDate"] = ToFscmDateLiteral(txDate);
        }

        ApplyPayloadHygiene(cloned);
        ApplyDescriptionAndReferenceFields(cloned, sourceLine, resolved.Fsa, planned.IsReversal);
        await ApplyQuantityAndDates(cloned, context, resolved, jt, woGuid, woNumber, period, todayUtc, qtyKey, planned, ct);
        ApplyDimensionAndWarehouse(cloned, planned, resolved.Fsa, jt);
        ApplyAmounts(cloned, planned, resolved.Fsa, fscmAgg);
        ComputeAndSetUnitAmount(cloned, qtyKey);
        return cloned;
    }

    private static void ApplyPayloadHygiene(JsonObject cloned)
    {
        RemoveLoose(cloned, "JournalDescription");
        RemoveLoose(cloned, Keys.JournalDescription);
        RemoveLoose(cloned, Keys.ModifiedOn);
        RemoveLoose(cloned, Keys.RpcAccrualFieldModifiedOn);
        RemoveLoose(cloned, Keys.LineNum);
        RemoveLoose(cloned, "LineNum");
        RemoveLoose(cloned, "LineNumber");
        RemoveLoose(cloned, "JournalName");
    }

    private static void ApplyDescriptionAndReferenceFields(JsonObject cloned, JsonObject sourceLine, FsaWorkOrderLineSnapshot fsa, bool isReversal)
    {
        if (isReversal)
        {
            var reversalDesc = GetStringLooseAny(cloned, "FSACustomerProductDesc");
            if (!string.IsNullOrWhiteSpace(reversalDesc))
                SetOrAddLoose(cloned, "FSACustomerProductDesc", reversalDesc);
            else
                RemoveLoose(cloned, "FSACustomerProductDesc");
        }
        else
        {
            var fsDesc = ResolveFsDescription(sourceLine) ?? string.Empty;
            SetOrAddLoose(cloned, "JournalLineDescription", fsDesc);
            SetOrAddLoose(cloned, "FSACustomerProductDesc", fsDesc);
        }

        var custProdId = ResolveFsCustomerProductId(cloned);
        if (!string.IsNullOrWhiteSpace(custProdId))
            SetOrAddLoose(cloned, "FSACustomerProductID", custProdId);
        else if (!string.IsNullOrWhiteSpace(fsa.CustomerProductId))
            SetOrAddLoose(cloned, "FSACustomerProductID", fsa.CustomerProductId);

        var taxabilityType = ResolveFsTaxability(cloned);
        if (!string.IsNullOrWhiteSpace(taxabilityType))
            SetOrAddLoose(cloned, "FSATaxabilityType", taxabilityType);
        else if (!string.IsNullOrWhiteSpace(fsa.TaxabilityType))
            SetOrAddLoose(cloned, "FSATaxabilityType", fsa.TaxabilityType);
    }

    private async Task ApplyQuantityAndDates(
        JsonObject cloned,
        RunContext context,
        ResolvedDeltaLine resolved,
        JournalType jt,
        Guid woGuid,
        string? woNumber,
        AccountingPeriodSnapshot period,
        DateTime todayUtc,
        string qtyKey,
        DeltaPlannedLine planned,
        CancellationToken ct)
    {
        if (jt == JournalType.Item || jt == JournalType.Expense)
            RemoveLoose(cloned, Keys.Duration);

        var workingUtc = ResolveWorkingDateForPlannedLine(planned, resolved.Fsa, todayUtc);
        var isClosed = await period.IsDateInClosedPeriodAsync(workingUtc, ct).ConfigureAwait(false);
        var txUtc = isClosed ? period.CurrentOpenPeriodStartDate.Date : workingUtc.Date;

        cloned[qtyKey] = planned.Quantity;
        cloned["OperationDate"] = ToFscmDateLiteral(workingUtc.Date);
        cloned[Keys.TransactionDate] = ToFscmDateLiteral(txUtc);
        cloned[Keys.RpcWorkingDate] = ToFscmDateLiteral(workingUtc.Date);

        await _aisLogger.InfoAsync(
            context.RunId,
            "Delta",
            "Accounting period classification (OpsDate-based).",
            new
            {
                context.CorrelationId,
                WorkOrderGuid = woGuid,
                WorkOrderNumber = woNumber,
                JournalType = jt.ToString(),
                WorkOrderLineGuid = resolved.WorkOrderLineId,
                RPCWorkingDate = workingUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                IsClosedOrOnHold = isClosed,
                CurrentOpenPeriodStartDate = period.CurrentOpenPeriodStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            },
            ct).ConfigureAwait(false);
    }

    private static DateTime ResolveWorkingDateForPlannedLine(
        DeltaPlannedLine planned,
        FsaWorkOrderLineSnapshot fsa,
        DateTime todayUtc)
    {
        if (planned.IsReversal)
        {
            var snap = planned.ReversalPayloadSnapshotOverride;
            if (snap?.OperationDate is DateTime snapOperationDate)
                return snapOperationDate.Date;

            if (snap?.TransactionDate is DateTime snapTransactionDate)
                return snapTransactionDate.Date;

            if (planned.TransactionDate != default)
                return planned.TransactionDate.Date;
        }

        return fsa.OperationsDateUtc?.Date ?? todayUtc.Date;
    }

    private static void ApplyDimensionAndWarehouse(JsonObject cloned, DeltaPlannedLine planned, FsaWorkOrderLineSnapshot fsa, JournalType jt)
    {
        var plannedDept = planned.Department;
        var plannedProd = planned.ProductLine;

        if (string.IsNullOrWhiteSpace(plannedDept) || string.IsNullOrWhiteSpace(plannedProd))
        {
            if (JsonLooseKey.TryGetStringLoose(cloned, "DimensionDisplayValue", out var existingDdv) &&
                TryParseDeptProdFromDimensionDisplayValue(existingDdv, out var ddDept2, out var ddProd2))
            {
                if (string.IsNullOrWhiteSpace(plannedDept)) plannedDept = ddDept2;
                if (string.IsNullOrWhiteSpace(plannedProd)) plannedProd = ddProd2;
            }

            if (string.IsNullOrWhiteSpace(plannedDept)) plannedDept = fsa.Department;
            if (string.IsNullOrWhiteSpace(plannedProd)) plannedProd = fsa.ProductLine;
        }

        var newDdv = BuildDefaultDimensionDisplayValue(plannedDept, plannedProd);
        if (!string.IsNullOrWhiteSpace(newDdv))
            SetOrAddLoose(cloned, "DimensionDisplayValue", newDdv);

        SetOrAddLoose(cloned, Keys.LineProperty, planned.LineProperty);
        RemoveLoose(cloned, Keys.DimDepartment);
        RemoveLoose(cloned, Keys.DimProduct);

        if (jt == JournalType.Item)
        {
            if (!string.IsNullOrWhiteSpace(planned.Warehouse))
                SetOrAddLoose(cloned, Keys.Warehouse, planned.Warehouse);
        }
        else
        {
            RemoveLoose(cloned, Keys.Warehouse);
            RemoveLoose(cloned, "Warehouse");
        }
    }

    private static void ApplyAmounts(JsonObject cloned, DeltaPlannedLine planned, FsaWorkOrderLineSnapshot fsa, FscmWorkOrderLineAggregation? fscmAgg)
    {
        if (planned.IsReversal && fscmAgg is not null)
        {
            var fscmPrice = ResolveFscmUnitPriceForReversal(fscmAgg);
            if (fscmPrice.HasValue)
            {
                SetOrAddLoose(cloned, Keys.UnitAmount, fscmPrice.Value);
                RemoveLoose(cloned, Keys.UnitCost);
                return;
            }
        }

        if (planned.CalculatedUnitPrice.HasValue)
        {
            SetOrAddLoose(cloned, Keys.UnitAmount, planned.CalculatedUnitPrice.Value);
            RemoveLoose(cloned, Keys.UnitCost);
        }
        else if (fsa.CalculatedUnitPrice.HasValue)
        {
            SetOrAddLoose(cloned, Keys.UnitAmount, fsa.CalculatedUnitPrice.Value);
            RemoveLoose(cloned, Keys.UnitCost);
        }
    }
}
