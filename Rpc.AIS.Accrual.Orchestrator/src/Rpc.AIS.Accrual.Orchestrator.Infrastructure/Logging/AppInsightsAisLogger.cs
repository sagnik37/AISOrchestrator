using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

/// <summary>
/// AIS logger implementation that writes structured logs through ILogger.
/// In Azure Functions, these logs flow to Application Insights when configured.
/// </summary>
public sealed class AppInsightsAisLogger : IAisLogger
{
    private readonly ILogger<AppInsightsAisLogger> _logger;

    public AppInsightsAisLogger(ILogger<AppInsightsAisLogger> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes info async.
    /// </summary>
    public Task InfoAsync(string runId, string step, string message, object? data, CancellationToken ct)
    {
        _logger.LogInformation("{Step} | RunId={RunId} | {Message} | {@Data}", step, runId, message, data);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Executes warn async.
    /// </summary>
    public Task WarnAsync(string runId, string step, string message, object? data, CancellationToken ct)
    {
        _logger.LogWarning("{Step} | RunId={RunId} | {Message} | {@Data}", step, runId, message, data);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Executes error async.
    /// </summary>
    public Task ErrorAsync(string runId, string step, string message, Exception? ex, object? data, CancellationToken ct)
    {
        _logger.LogError(ex, "{Step} | RunId={RunId} | {Message} | {@Data}", step, runId, message, data);
        return Task.CompletedTask;
    }
}
