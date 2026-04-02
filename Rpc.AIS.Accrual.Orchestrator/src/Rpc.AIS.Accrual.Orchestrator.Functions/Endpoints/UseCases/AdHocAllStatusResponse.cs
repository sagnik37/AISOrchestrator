namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

public sealed record AdHocAllStatusResponse(
    string Status,
    string InstanceId,
    string? RuntimeStatus,
    string? CreatedAtUtc,
    string? LastUpdatedAtUtc,
    string? SerializedInput,
    string? SerializedOutput,
    string? FailureDetails,
    bool Exists);
