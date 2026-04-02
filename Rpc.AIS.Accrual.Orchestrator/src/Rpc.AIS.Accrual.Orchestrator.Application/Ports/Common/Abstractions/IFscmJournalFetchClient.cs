using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Fetches journal lines from FSCM (OData) for a given Work Order.
/// Used for delta calculation (group by WorkOrderLineId).
/// </summary>
public interface IFscmJournalFetchClient
{
    /// <summary>
    /// Fetch journal lines from FSCM for a set of WorkOrder GUIDs (OR-filter batched) for a given JournalType.
    ///
    /// Implementations MUST chunk the input IDs to avoid URL-length / OData parsing limits.
    /// </summary>
    Task<IReadOnlyList<FscmJournalLine>> FetchByWorkOrdersAsync(
        RunContext context,
        JournalType journalType,
        IReadOnlyCollection<Guid> workOrderIds,
        CancellationToken ct);
}
