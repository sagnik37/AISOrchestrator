namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Defines i run id generator behavior.
/// </summary>
public interface IRunIdGenerator
{
    string NewRunId();
    string NewCorrelationId();
}
