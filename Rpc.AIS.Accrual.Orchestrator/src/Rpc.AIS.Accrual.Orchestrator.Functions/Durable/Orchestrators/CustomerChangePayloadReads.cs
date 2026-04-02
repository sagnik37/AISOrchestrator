using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services;

internal static class CustomerChangePayloadReads
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null
    };

    public static (string? Company, string? WorkOrderId) TryReadCompanyAndWorkOrderId(string woPayloadJson, Guid workOrderGuid, ILogger log, RunContext ctx)
        {
            try
            {
                var node = JsonNode.Parse(woPayloadJson);
                if (node is null) return (null, null);

                var req = node["_request"] as JsonObject;
                var woList = req?["WOList"] as JsonArray;
                if (woList is null) return (null, null);

                foreach (var wo in woList.OfType<JsonObject>())
                {
                    var guidStr = wo["WorkOrderGUID"]?.ToString();
                    if (!Guid.TryParse(guidStr?.Trim('{', '}'), out var g) || g != workOrderGuid)
                        continue;

                    var company = wo["Company"]?.ToString();
                    var woId = wo["WorkOrderID"]?.ToString();
                    return (company, woId);
                }
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "CustomerChange: Failed to parse WO payload for Company/WorkOrderId. RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid}", ctx.RunId, ctx.CorrelationId, workOrderGuid);
            }

            return (null, null);
        }

        public static string RewriteSubProjectId(string woPayloadJson, string subProjectId, ILogger log, RunContext ctx)
        {
            if (string.IsNullOrWhiteSpace(woPayloadJson)) return woPayloadJson;
            if (string.IsNullOrWhiteSpace(subProjectId)) return woPayloadJson;

            JsonNode? node;
            try { node = JsonNode.Parse(woPayloadJson); }
            catch (Exception ex)
            {
                log.LogWarning(ex, "CustomerChange: Failed to parse WO payload JSON while rewriting SubProjectId. RunId={RunId} CorrelationId={CorrelationId}", ctx.RunId, ctx.CorrelationId);
                return woPayloadJson;
            }

            if (node is not JsonObject root) return woPayloadJson;
            if (!root.TryGetPropertyValue("_request", out var reqNode) || reqNode is not JsonObject reqObj) return woPayloadJson;
            if (!reqObj.TryGetPropertyValue("WOList", out var listNode) || listNode is not JsonArray woList) return woPayloadJson;

            foreach (var wo in woList.OfType<JsonObject>())
            {
                wo["SubProjectId"] = subProjectId;
            }

            return root.ToJsonString(JsonOpts);
        }
}
