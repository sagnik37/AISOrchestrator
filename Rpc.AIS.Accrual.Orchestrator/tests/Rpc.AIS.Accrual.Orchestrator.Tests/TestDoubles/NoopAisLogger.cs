using System;
using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

namespace Rpc.AIS.Accrual.Orchestrator.Tests.TestDoubles;

/// <summary>
/// Minimal no-op implementation for tests.
/// </summary>
internal sealed class NoopAisLogger : IAisLogger
{
    public Task InfoAsync(string runId, string step, string message, object? data, CancellationToken ct)
        => Task.CompletedTask;

    public Task WarnAsync(string runId, string step, string message, object? data, CancellationToken ct)
        => Task.CompletedTask;

    public Task ErrorAsync(string runId, string step, string message, Exception? ex, object? data, CancellationToken ct)
        => Task.CompletedTask;
}
