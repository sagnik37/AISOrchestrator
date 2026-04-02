using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using System.Threading;
using System.Threading.Tasks;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Notifications;

/// <summary>
/// Placeholder email sender. Replace with Graph API, SendGrid, SMTP relay, Logic App, etc.
/// </summary>
public sealed class NoopEmailSender : IEmailSender
{
    /// <summary>
    /// Executes send async.
    /// </summary>
    public Task SendAsync(string subject, string htmlBody, IReadOnlyList<string> to, CancellationToken ct)
    {
        Console.WriteLine($"EMAIL (noop) TO={string.Join(';', to)} SUBJECT={subject} BODYLEN={htmlBody?.Length ?? 0}");
        return Task.CompletedTask;
    }
}
