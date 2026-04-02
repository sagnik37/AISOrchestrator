using System;
using System.Text.Json;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

public interface IFsaServiceLineMapper
{
    /// <summary>
    /// Maps a Dataverse Work Order Service row into a <see cref="FsaServiceLine"/>.
    /// </summary>
    FsaServiceLine Map(
        JsonElement row,
        Guid workOrderId,
        string workOrderNumber);
}
