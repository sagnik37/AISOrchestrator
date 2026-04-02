namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.FscmJournalPolicies;

using System.Text.Json;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

public sealed class HourJournalFetchPolicy : FscmJournalFetchPolicyBase
{
    private const string WorkOrderIdField = "RPCWorkOrderGuid";
    private const string WorkOrderLineIdField = "RPCWorkOrderLineGuid";

    public override JournalType JournalType => JournalType.Hour;
    public override string EntitySet => "JournalTrans";

    public override string Select => string.Join(",",
    WorkOrderIdField,
    WorkOrderLineIdField,
    "ProjectID",
    "Hours",
    "SalesPrice",
    "LineProperty",
    "DimensionDisplayValue",
    "ProjectDate"
);


    public override decimal GetQuantity(JsonElement row) =>
        TryGetDecimal(row, "Hours")
        ?? TryGetDecimal(row, "Qty")
        ?? TryGetDecimal(row, "Quantity")
        ?? 0m;

    public override decimal? GetUnitPrice(JsonElement row) =>
        TryGetDecimal(row, "SalesPrice")
        ?? TryGetDecimal(row, "ProjectSalesPrice");
}
