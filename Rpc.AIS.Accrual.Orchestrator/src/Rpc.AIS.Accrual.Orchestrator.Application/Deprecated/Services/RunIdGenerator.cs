using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services;

/// <summary>
/// Generates run identifiers and correlation identifiers. Uses compact GUIDs.
/// </summary>
public sealed class RunIdGenerator : IRunIdGenerator
{
    public string NewRunId() => $"RUN-{Guid.NewGuid():N}";
    public string NewCorrelationId() => $"CORR-{Guid.NewGuid():N}";
}
