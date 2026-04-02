using System;
using System.Collections.Generic;
using System.Text.Json;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

public interface IFsaSnapshotBuilder
{
    IReadOnlyList<FsaDeltaSnapshot> BuildSnapshots(
        IReadOnlyList<Guid> impactedWoIds,
        Dictionary<Guid, string> woNumberById,
        Dictionary<Guid, string?> woCompanyById,
        JsonDocument woProducts,
        JsonDocument woServices,
        Dictionary<Guid, string> productTypeById,
        Dictionary<Guid, string?> itemNumberById);
}
