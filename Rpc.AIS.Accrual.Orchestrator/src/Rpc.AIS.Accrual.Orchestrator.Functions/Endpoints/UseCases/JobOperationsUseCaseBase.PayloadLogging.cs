using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;
using Microsoft.Azure.Functions.Worker.Http;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

public abstract partial class JobOperationsUseCaseBase
{
    protected async Task LogInboundPayloadAsync(string runId, string correlationId, string operation, string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return;

        var (woGuid, woId, woCount) = TryGetFirstWorkOrderIdentity(body);

        await _aisLogger.LogJsonPayloadAsync(
            runId: runId,
            step: $"{operation}_INBOUND",
            message: "Inbound request payload received",
            payloadType: "FS_REQUEST",
            workOrderGuid: woGuid,
            workOrderNumber: woId,
            json: body,
            logBody: _diag.LogPayloadBodies && (_diag.LogMultiWoPayloadBody || woCount <= 1),
            snippetChars: _diag.PayloadSnippetChars,
            chunkChars: _diag.PayloadChunkChars,
            ct: default).ConfigureAwait(false);
    }

    private static (string WorkOrderGuid, string? WorkOrderId, int WoCount) TryGetFirstWorkOrderIdentity(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("_request", out var req) || req.ValueKind != JsonValueKind.Object)
                return ("MULTI", null, 0);
            if (!req.TryGetProperty("WOList", out var list) || list.ValueKind != JsonValueKind.Array)
                return ("MULTI", null, 0);

            var count = list.GetArrayLength();
            using var e = list.EnumerateArray();
            if (!e.MoveNext()) return ("MULTI", null, count);
            var wo = e.Current;

            static string? ReadString(JsonElement obj, string prop)
            {
                if (obj.ValueKind != JsonValueKind.Object) return null;
                if (obj.TryGetProperty(prop, out var v))
                {
                    if (v.ValueKind == JsonValueKind.String) return v.GetString();
                    if (v.ValueKind == JsonValueKind.Number || v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) return v.ToString();
                }
                return null;
            }

            var guid = ReadString(wo, "WorkOrderGUID") ?? ReadString(wo, "WorkOrderGuid") ?? ReadString(wo, "workOrderGuid");
            var id = ReadString(wo, "WorkOrderID") ?? ReadString(wo, "WorkOrderId") ?? ReadString(wo, "workOrderId") ?? ReadString(wo, "WONumber");

            if (string.IsNullOrWhiteSpace(guid)) return (count <= 1 ? "UNKNOWN" : "MULTI", id, count);
            return (guid.Trim(), id, count);
        }
        catch
        {
            return ("MULTI", null, 0);
        }
    }
}
