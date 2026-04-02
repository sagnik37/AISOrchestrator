using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.Mappers;

public sealed class FsaSnapshotBuilder : IFsaSnapshotBuilder
{
    private readonly IFsaProductLineMapper _productMapper;
    private readonly IFsaServiceLineMapper _serviceMapper;

    public FsaSnapshotBuilder(IFsaProductLineMapper productMapper, IFsaServiceLineMapper serviceMapper)
    {
        _productMapper = productMapper ?? throw new ArgumentNullException(nameof(productMapper));
        _serviceMapper = serviceMapper ?? throw new ArgumentNullException(nameof(serviceMapper));
    }

    public IReadOnlyList<FsaDeltaSnapshot> BuildSnapshots(
        IReadOnlyList<Guid> impactedWoIds,
        Dictionary<Guid, string> woNumberById,
        Dictionary<Guid, string?> woCompanyById,
        JsonDocument woProducts,
        JsonDocument woServices,
        Dictionary<Guid, string> productTypeById,
        Dictionary<Guid, string?> itemNumberById)
    {
        var byWo = impactedWoIds.ToDictionary(
            id => id,
            id => (inv: new List<FsaProductLine>(), noninv: new List<FsaProductLine>(), svc: new List<FsaServiceLine>()));

        if (woProducts != null &&
            woProducts.RootElement.TryGetProperty("value", out var pArr) &&
            pArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in pArr.EnumerateArray())
            {
                if (!FsaDeltaPayloadJsonHelpers.TryGuid(row, "_msdyn_workorder_value", out var woId)) continue;
                if (!byWo.ContainsKey(woId)) continue;

                var woNo = woNumberById.TryGetValue(woId, out var n) ? n : woId.ToString();
                woCompanyById.TryGetValue(woId, out var company);

                var (line, isInv) = _productMapper.Map(row, woId, woNo, productTypeById, itemNumberById);

                line = line with
                {
                    DataAreaId = line.DataAreaId ?? company
                };

                if (isInv) byWo[woId].inv.Add(line);
                else byWo[woId].noninv.Add(line);
            }
        }

        if (woServices != null &&
            woServices.RootElement.TryGetProperty("value", out var sArr) &&
            sArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in sArr.EnumerateArray())
            {
                if (!FsaDeltaPayloadJsonHelpers.TryGuid(row, "_msdyn_workorder_value", out var woId)) continue;
                if (!byWo.ContainsKey(woId)) continue;

                var woNo = woNumberById.TryGetValue(woId, out var n) ? n : woId.ToString();
                woCompanyById.TryGetValue(woId, out var company);

                var line = _serviceMapper.Map(row, woId, woNo);

                var stamped = line with
                {
                    DataAreaId = line.DataAreaId ?? company
                };

                byWo[woId].svc.Add(stamped);
            }
        }

        return byWo.Select(kvp =>
            new FsaDeltaSnapshot(
                WorkOrderNumber: woNumberById.TryGetValue(kvp.Key, out var n) ? n : kvp.Key.ToString(),
                WorkOrderId: kvp.Key,
                InventoryProducts: kvp.Value.inv,
                NonInventoryProducts: kvp.Value.noninv,
                ServiceLines: kvp.Value.svc)).ToList();
    }
}
