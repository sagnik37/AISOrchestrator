namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Defines i dataverse token provider behavior.
/// </summary>
public interface IDataverseTokenProvider
{
    Task<string> GetAccessTokenAsync(string resource, CancellationToken ct);
}
