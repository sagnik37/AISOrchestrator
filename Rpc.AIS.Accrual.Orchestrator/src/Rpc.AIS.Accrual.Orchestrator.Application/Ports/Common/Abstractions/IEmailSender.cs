namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Defines i email sender behavior.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string subject, string htmlBody, IReadOnlyList<string> to, CancellationToken ct);
}
