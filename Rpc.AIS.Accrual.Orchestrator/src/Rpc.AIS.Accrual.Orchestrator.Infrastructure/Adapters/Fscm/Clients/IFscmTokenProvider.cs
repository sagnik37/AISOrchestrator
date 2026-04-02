namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Defines i fscm token provider behavior.
/// </summary>
public interface IFscmTokenProvider
{
    Task<string> GetAccessTokenAsync(string scope, CancellationToken ct);
}
