namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Submit acknowledgement contract for AdHoc batch-all requests.
/// Keeps the HTTP layer explicit while the bulk run continues asynchronously.
/// </summary>
public sealed record AdHocAllAcceptedResponse(
    string Status,
    string Message,
    string RunId,
    string CorrelationId,
    string SourceSystem,
    string Trigger,
    string BatchId,
    string OrchestrationInstanceId,
    string TrackingMode,
    string? RuntimeStatus = null,
    string? StatusQueryRoute = null);
