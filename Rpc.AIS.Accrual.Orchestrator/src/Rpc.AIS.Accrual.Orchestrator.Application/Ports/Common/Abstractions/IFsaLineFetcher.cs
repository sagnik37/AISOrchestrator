using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Defines i fsa line fetcher behavior.
/// </summary>
public interface IFsaLineFetcher
{
    Task<JsonDocument> GetOpenWorkOrdersAsync(RunContext context, CancellationToken ct);

    Task<JsonDocument> GetWorkOrdersAsync(RunContext context, List<string> workOrderIds, CancellationToken ct);

    Task<JsonDocument> GetWorkOrderProductsAsync(RunContext context, List<string> workOrderIds, CancellationToken ct);

    Task<JsonDocument> GetWorkOrderServicesAsync(RunContext context, List<string> workOrderIds, CancellationToken ct);

    /// <summary>
    /// Lightweight presence query: returns WorkOrderIds that have at least one product line.
    /// This must not fetch full product lines.
    /// </summary>
    Task<HashSet<string>> GetWorkOrderIdsWithProductsAsync(RunContext context, List<string> workOrderIds, CancellationToken ct);

    /// <summary>
    /// Lightweight presence query: returns WorkOrderIds that have at least one service line.
    /// This must not fetch full service lines.
    /// </summary>
    Task<HashSet<string>> GetWorkOrderIdsWithServicesAsync(RunContext context, List<string> workOrderIds, CancellationToken ct);

    Task<JsonDocument> GetProductsAsync(RunContext context, IReadOnlyList<Guid> productIds, CancellationToken ct);
}
