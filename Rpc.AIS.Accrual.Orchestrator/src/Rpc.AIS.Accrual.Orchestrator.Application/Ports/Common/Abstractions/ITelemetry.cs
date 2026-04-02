namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

public interface ITelemetry
{
    void LogJson(string eventName, string runId, string correlationId, string? workOrderNumber, string json, int chunkSize = 7000);
}
