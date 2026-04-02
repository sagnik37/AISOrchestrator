using System;
using System.Collections.Generic;
using System.Text.Json;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

public interface IFsaProductLineMapper
{
    /// <summary>
    /// Maps a Dataverse Work Order Product row into a <see cref="FsaProductLine"/>.
    /// Returns the mapped line and a flag indicating whether it should be treated as Inventory.
    /// </summary>
    (FsaProductLine Line, bool IsInventory) Map(
        JsonElement row,
        Guid workOrderId,
        string workOrderNumber,
        Dictionary<Guid, string> productTypeById,
        Dictionary<Guid, string?> itemNumberById);
}
