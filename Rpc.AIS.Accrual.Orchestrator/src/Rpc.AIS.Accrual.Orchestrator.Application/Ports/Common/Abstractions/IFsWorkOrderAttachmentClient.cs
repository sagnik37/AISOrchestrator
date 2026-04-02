using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Reads work-order related document attachments from Field Service (Dataverse).
/// </summary>
public interface IFsWorkOrderAttachmentClient
{
    Task<IReadOnlyList<WorkOrderAttachment>> GetForWorkOrderAsync(
        RunContext ctx,
        Guid workOrderGuid,
        CancellationToken ct);
}
