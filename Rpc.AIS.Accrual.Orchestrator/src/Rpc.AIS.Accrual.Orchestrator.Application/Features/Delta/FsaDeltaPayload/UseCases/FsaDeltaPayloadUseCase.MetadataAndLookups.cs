// File: .../Core/UseCases/FsaDeltaPayload/*

// - Moves delta payload orchestration into Core (UseCase layer) and splits the  orchestrator into partials.
// - Functions layer becomes a thin adapter.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using static Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.FsaDeltaPayloadJsonUtil;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;

namespace Rpc.AIS.Accrual.Orchestrator.Core.UseCases.FsaDeltaPayload;

public sealed partial class FsaDeltaPayloadUseCase
{
    private static Dictionary<Guid, string> BuildWorkOrderNumberMap(JsonDocument woHeaders)
    {
        var map = new Dictionary<Guid, string>();

        if (woHeaders is null)
            return map;

        if (!woHeaders.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return map;

        foreach (var row in arr.EnumerateArray())
        {
            if (!TryGuid(row, "msdyn_workorderid", out var id)) continue;
            var num = row.TryGetProperty("msdyn_name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            if (!string.IsNullOrWhiteSpace(num))
                map[id] = num!;
        }

        return map;
    }

    /// <summary>
    /// Executes collect lookup ids.
    /// </summary>
    private static void CollectLookupIds(JsonDocument doc, HashSet<Guid> target, params string[] keys)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (keys is null || keys.Length == 0) throw new ArgumentException("At least one key is required.", nameof(keys));

        if (!doc.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;

        foreach (var row in arr.EnumerateArray())
        {
            foreach (var k in keys)
            {
                if (!row.TryGetProperty(k, out var p)) continue;

                if (p.ValueKind == JsonValueKind.String &&
                    Guid.TryParse(p.GetString(), out var g))
                {
                    target.Add(g);
                    break;
                }
            }
        }
    }

    private static (Dictionary<Guid, string> productTypeById, Dictionary<Guid, string?> itemNumberById)
        BuildProductEnrichmentMaps(JsonDocument products)
    {
        var typeMap = new Dictionary<Guid, string>();
        var itemMap = new Dictionary<Guid, string?>();

        if (products is null)
            return (typeMap, itemMap);

        if (!products.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return (typeMap, itemMap);

        foreach (var row in arr.EnumerateArray())
        {
            if (!TryGuid(row, "productid", out var pid)) continue;

            int? os = row.TryGetProperty("msdyn_fieldserviceproducttype", out var osp) && osp.ValueKind == JsonValueKind.Number
                ? osp.GetInt32()
                : null;

            var ptype = os == 690970000 ? "Inventory"
                     : os == 690970001 ? "Non-Inventory"
                     : "Unknown";

            typeMap[pid] = ptype;
            itemMap[pid] = row.TryGetProperty("msdyn_productnumber", out var inum) && inum.ValueKind == JsonValueKind.String ? inum.GetString() : null;
        }

        return (typeMap, itemMap);
    }
}
