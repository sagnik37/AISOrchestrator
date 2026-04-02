using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Defines i single work order posting client behavior.
/// </summary>
public interface ISingleWorkOrderPostingClient
{
    Task<PostSingleWorkOrderResponse> PostAsync(string rawJsonBody, CancellationToken ct);
    // NEW (preferred)
    Task<PostSingleWorkOrderResponse> PostAsync(RunContext context, string rawJsonBody, CancellationToken ct);
}

/// <summary>
/// Carries post single work order response data.
/// </summary>
public sealed record PostSingleWorkOrderResponse(bool IsSuccess, int StatusCode, string? ResponseBody);
