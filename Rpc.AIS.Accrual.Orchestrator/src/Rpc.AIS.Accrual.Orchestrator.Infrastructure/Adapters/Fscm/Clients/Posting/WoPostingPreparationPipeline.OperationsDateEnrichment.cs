using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;

public sealed partial class WoPostingPreparationPipeline
{
    private async Task<string> EnrichMissingOperationsDatesFromFsaAsync(
        RunContext ctx,
        string woPayloadJson,
        CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(woPayloadJson);

        if (!doc.RootElement.TryGetProperty("_request", out var req) || !req.TryGetProperty("WOList", out var woList))
            return woPayloadJson;

        var workOrderIds = woList.EnumerateArray()
            .Select(x => x.GetProperty("WorkOrderGUID").GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        if (workOrderIds.Count == 0)
            return woPayloadJson;

        var products = await _fsaLineFetcher!.GetWorkOrderProductsAsync(ctx, workOrderIds!, ct).ConfigureAwait(false);
        var services = await _fsaLineFetcher!.GetWorkOrderServicesAsync(ctx, workOrderIds!, ct).ConfigureAwait(false);

        var map = BuildOpsMap(products, "msdyn_workorderproductid");
        foreach (var kv in BuildOpsMap(services, "msdyn_workorderserviceid"))
            map[kv.Key] = kv.Value;

        var rootNode = JsonNode.Parse(woPayloadJson);
        var nodeWoList = rootNode?["_request"]?["WOList"]?.AsArray();
        if (nodeWoList is null) return woPayloadJson;

        foreach (var wo in nodeWoList)
        {
            InjectOpsDate(wo, "WOItemLines", map);
            InjectOpsDate(wo, "WOHourLines", map);
            InjectOpsDate(wo, "WOExpLines", map);
        }

        return rootNode!.ToJsonString();
    }

    private static Dictionary<Guid, string> BuildOpsMap(JsonDocument doc, string idField)
    {
        var dict = new Dictionary<Guid, string>();

        if (!doc.RootElement.TryGetProperty("value", out var arr))
            return dict;

        foreach (var row in arr.EnumerateArray())
        {
            if (!row.TryGetProperty(idField, out var idEl)) continue;
            if (!Guid.TryParse(idEl.GetString()?.Trim('{', '}'), out var id)) continue;

            if (row.TryGetProperty("rpc_operationsdate", out var dateEl))
            {
                var date = dateEl.GetString();
                if (!string.IsNullOrWhiteSpace(date))
                    dict[id] = date!;
            }
        }

        return dict;
    }

    private static void InjectOpsDate(JsonNode? woNode, string sectionName, Dictionary<Guid, string> map)
    {
        var lines = woNode?[sectionName]?["JournalLines"]?.AsArray();
        if (lines is null) return;

        foreach (var ln in lines)
        {
            var obj = ln?.AsObject();
            if (obj is null) continue;

            var rawGuid = obj["WorkOrderLineGuid"]?.GetValue<string>();
            if (!Guid.TryParse(rawGuid?.Trim('{', '}'), out var id)) continue;
            if (!map.TryGetValue(id, out var rawDate)) continue;

            var literal = NormalizeDate(rawDate);
            if (literal is null) continue;

            obj.Remove("rpc_operationsdate");
            obj.Remove("rpc_OperationsDate");

            if (string.IsNullOrWhiteSpace(obj["OperationDate"]?.GetValue<string>()))
                obj["OperationDate"] = literal;

            obj.Remove("RPCWorkingDate");

            if (string.IsNullOrWhiteSpace(obj["TransactionDate"]?.GetValue<string>()))
                obj["TransactionDate"] = literal;
        }
    }

    private static string? NormalizeDate(string raw)
    {
        if (!DateTimeOffset.TryParse(raw, out var dto))
            return null;

        var utc = new DateTime(dto.Year, dto.Month, dto.Day, 0, 0, 0, DateTimeKind.Utc);
        var ms = new DateTimeOffset(utc).ToUnixTimeMilliseconds();
        return $"/Date({ms})/";
    }
}
