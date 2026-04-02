using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using System.Net.Mail;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Notifications;

/// <summary>
/// Azure Communication Services Email sender.
/// This implementation is designed to be safe in production:
/// - Honors CancellationToken
/// - Validates recipients
/// - Logs failures
/// - Does NOT throw on send failure (so orchestration isn't failed by notification issues)
/// </summary>
public sealed class AcsEmailSender : IEmailSender
{
    private readonly EmailClient _client;
    private readonly AcsEmailOptions _opt;
    private readonly ILogger<AcsEmailSender> _logger;

    public AcsEmailSender(EmailClient client, AcsEmailOptions opt, ILogger<AcsEmailSender> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _opt = opt ?? throw new ArgumentNullException(nameof(opt));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes send async.
    /// </summary>
    public async Task SendAsync(string subject, string htmlBody, IReadOnlyList<string> to, CancellationToken ct)
    {
        if (!_opt.Enabled)
        {
            _logger.LogDebug("ACS email is disabled (AcsEmail:Enabled=false). Skipping send.");
            return;
        }

        if (to is null || to.Count == 0)
        {
            _logger.LogDebug("ACS email send skipped: no recipients.");
            return;
        }

        var recipients = to
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (recipients.Count == 0)
        {
            _logger.LogDebug("ACS email send skipped: recipients were blank after normalization.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_opt.FromAddress))
        {
            _logger.LogWarning("ACS email send skipped: AcsEmail:FromAddress is not configured.");
            return;
        }

        ct.ThrowIfCancellationRequested();

        //var from = string.IsNullOrWhiteSpace(_opt.FromDisplayName)
        //    ? _opt.FromAddress
        //    : $"{_opt.FromDisplayName} <{_opt.FromAddress}>";
        var from = _opt.FromAddress.Trim();

        var emailContent = new EmailContent(subject ?? string.Empty)
        {
            Html = htmlBody ?? string.Empty
        };

        var emailRecipients = new EmailRecipients(
            recipients.Select(r => new EmailAddress(r)).ToList()
        );

        var message = new EmailMessage(from, emailRecipients, emailContent);

        try
        {
            var waitUntil = _opt.WaitUntilCompleted ? WaitUntil.Completed : WaitUntil.Started;

            _logger.LogInformation(
                "Sending ACS email. ToCount={ToCount}, SubjectLength={SubjectLength}, WaitUntil={WaitUntil}",
                recipients.Count,
                subject?.Length ?? 0,
                waitUntil.ToString());

            var op = await _client.SendAsync(waitUntil, message, ct);

            // op.Id is available; status may depend on WaitUntil mode
            _logger.LogInformation("ACS email send accepted. OperationId={OperationId}", op.Id);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("ACS email send cancelled.");
            throw;
        }
        catch (RequestFailedException ex)
        {
            // ACS-specific failures
            _logger.LogError(ex, "ACS email send failed (RequestFailedException). Code={Code}, Status={Status}",
                ex.ErrorCode, ex.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ACS email send failed (unexpected exception).");
        }
    }
}
