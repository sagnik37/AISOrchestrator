using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services;

/// <summary>
/// Abstractions used by Functions-layer Durable Activities for job operations.
/// Implementations are registered in DI. Some lifecycle steps may still be NOOP until FSCM endpoints are finalized.
/// </summary>
public interface IFscmProjectLifecycle
{
    Task SetProjectStageAsync(RunContext ctx, Guid workOrderGuid, string stage, CancellationToken ct);

    Task SyncJobAttributesToProjectAsync(RunContext ctx, Guid workOrderGuid, CancellationToken ct);
}

public interface ICustomerChangeOrchestrator
{
    /// <summary>
    /// Executes the full Customer Change business process and returns the new SubProjectId.
    /// </summary>
    Task<CustomerChangeResultDto> ExecuteAsync(RunContext ctx, Guid workOrderGuid, string rawRequestJson, CancellationToken ct);
}

public sealed record CustomerChangeResultDto(string NewSubProjectId);


/// <summary>
/// Safe placeholder implementation: logs intent, does not call external systems.
/// </summary>
public sealed class NoopFscmProjectLifecycle : IFscmProjectLifecycle
{
    private readonly ILogger<NoopFscmProjectLifecycle> _log;

    public NoopFscmProjectLifecycle(ILogger<NoopFscmProjectLifecycle> log)
        => _log = log ?? throw new ArgumentNullException(nameof(log));

    public Task SetProjectStageAsync(RunContext ctx, Guid workOrderGuid, string stage, CancellationToken ct)
    {
        _log.LogWarning(
            "NOOP: SetProjectStageAsync skipped (endpoints not configured). WorkOrderGuid={WorkOrderGuid} Stage={Stage} RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem}",
            workOrderGuid, stage, ctx.RunId, ctx.CorrelationId, ctx.SourceSystem);
        return Task.CompletedTask;
    }

    public Task SyncJobAttributesToProjectAsync(RunContext ctx, Guid workOrderGuid, CancellationToken ct)
    {
        _log.LogWarning(
            "NOOP: SyncJobAttributesToProjectAsync skipped (endpoints not configured). WorkOrderGuid={WorkOrderGuid} RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem}",
            workOrderGuid, ctx.RunId, ctx.CorrelationId, ctx.SourceSystem);
        return Task.CompletedTask;
    }
}

// <summary>
// Safe placeholder implementation: logs intended customer-change steps, does not call external systems.
// </summary>
