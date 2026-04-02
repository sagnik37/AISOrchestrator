// File: .../Core/UseCases/FsaDeltaPayload/*
//
// - Moves delta payload orchestration into Core (UseCase layer) and splits the  orchestrator into partials.
// - Functions layer becomes a thin adapter.


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.FsaDeltaPayloadJsonUtil;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload;

internal static class FsaDeltaPayloadLookupMaps
{
        internal static Dictionary<Guid, FsLineExtras> BuildLineExtrasMapForFinalPayload(
            JsonDocument woProducts,
            JsonDocument woServices)
        {
            var map = new Dictionary<Guid, FsLineExtras>();static void Add(
                JsonDocument doc,
                string idProp,
                bool includeWarehouseAndSite,
                Dictionary<Guid, FsLineExtras> target)
            {
                if (doc is null) return;

                if (!doc.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
                    return;

                foreach (var row in arr.EnumerateArray())
                {
                    if (!FsaDeltaPayloadJsonValueReaders.TryGuid(row, idProp, out var id))
                        continue;
                var extras = new FsLineExtras(
                   Currency: TryReadIsoCurrencyCode(row),
                    WorkerNumber: FsaDeltaPayloadJsonValueReaders.TryString(row, "cdm_workernumber"),
                    WarehouseIdentifier: includeWarehouseAndSite ? FsaDeltaPayloadJsonValueReaders.TryString(row, "msdyn_warehouseidentifier") : null,
                    SiteId: includeWarehouseAndSite ? FsaDeltaPayloadJsonValueReaders.TryString(row, "msdyn_siteid") : null,
                    LineNum: FsaDeltaPayloadJsonValueReaders.TryInt(row, "msdyn_lineorder"),
                    OperationsDate: FsaDeltaPayloadJsonValueReaders.TryString(row, "rpc_operationsdate") // NEW
);


                if (extras.HasAny())
                        target[id] = extras;
                }
            }

            // Products: category based on _msdyn_product_value (or sometimes _productid_value)
            Add(
                woProducts,
                idProp: "msdyn_workorderproductid",
                includeWarehouseAndSite: true,
                target: map);

            // Services: can be _msdyn_service_value OR sometimes _msdyn_product_value / _productid_value
            Add(
                woServices,
                idProp: "msdyn_workorderserviceid",
                includeWarehouseAndSite: false,
                target: map);

            return map;
        }
    static string? TryReadIsoCurrencyCode(JsonElement row)
    {
        // Preferred (legacy / flat)
        var flat = FsaDeltaPayloadJsonValueReaders.TryString(row, "isocurrencycode");
        if (!string.IsNullOrWhiteSpace(flat)) return flat;

        // Common Dataverse shape when expanded: transactioncurrencyid.isocurrencycode
        if (row.TryGetProperty("transactioncurrencyid", out var cur) && cur.ValueKind == JsonValueKind.Object)
        {
            var nested = FsaDeltaPayloadJsonValueReaders.TryString(cur, "isocurrencycode");
            if (!string.IsNullOrWhiteSpace(nested)) return nested;
        }

        return null;
    }
}
