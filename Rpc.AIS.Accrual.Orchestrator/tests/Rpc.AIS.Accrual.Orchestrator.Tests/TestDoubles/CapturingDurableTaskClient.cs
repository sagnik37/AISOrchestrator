// File: tests/Rpc.AIS.Accrual.Orchestrator.Tests/TestDoubles/CapturingDurableTaskClient.cs
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;

namespace Rpc.AIS.Accrual.Orchestrator.Tests.TestDoubles;

/// <summary>
/// DurableTaskClient test double that captures inputs for ScheduleNewOrchestrationInstanceAsync.
/// Implemented defensively to tolerate DurableTaskClient/OrchestrationMetadata signature drift.
/// </summary>
internal sealed class CapturingDurableTaskClient : DurableTaskClient
{
    internal TaskName? CapturedTaskName { get; private set; }
    internal object? CapturedInput { get; private set; }
    internal StartOrchestrationOptions? CapturedOptions { get; private set; }
    internal string? CapturedInstanceId { get; private set; }

    public CapturingDurableTaskClient(string name = "tests")
        : base(name)
    {
    }

    public override Task<string> ScheduleNewOrchestrationInstanceAsync(
        TaskName orchestratorName,
        object? input = null,
        StartOrchestrationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        CapturedTaskName = orchestratorName;
        CapturedInput = input;
        CapturedOptions = options;

        CapturedInstanceId = options?.InstanceId ?? $"test-{Guid.NewGuid():D}";
        return Task.FromResult(CapturedInstanceId);
    }

    public override Task<OrchestrationMetadata?> GetInstancesAsync(
        string instanceId,
        bool getInputsAndOutputs = false,
        CancellationToken cancellationToken = default)
        => Task.FromResult<OrchestrationMetadata?>(null);

    public override Microsoft.DurableTask.AsyncPageable<OrchestrationMetadata> GetAllInstancesAsync(
        OrchestrationQuery? query = null)
        => Microsoft.DurableTask.Pageable.Create<OrchestrationMetadata>(
            async (string? continuationToken, int? pageSizeHint, CancellationToken ct) =>
            {
                await Task.CompletedTask;
                return new Microsoft.DurableTask.Page<OrchestrationMetadata>(
                    values: Array.Empty<OrchestrationMetadata>(),
                    continuationToken: null);
            });

    public override Task<OrchestrationMetadata> WaitForInstanceStartAsync(
        string instanceId,
        bool getInputsAndOutputs = false,
        CancellationToken cancellationToken = default)
        => Task.FromResult(CreateMetadata(instanceId));

    public override Task<OrchestrationMetadata> WaitForInstanceCompletionAsync(
        string instanceId,
        bool getInputsAndOutputs = false,
        CancellationToken cancellationToken = default)
        => Task.FromResult(CreateMetadata(instanceId));

    public override Task RaiseEventAsync(
        string instanceId,
        string eventName,
        object? eventData = null,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public override Task SuspendInstanceAsync(
        string instanceId,
        string? reason = null,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public override Task ResumeInstanceAsync(
        string instanceId,
        string? reason = null,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static OrchestrationMetadata CreateMetadata(string instanceId)
    {
        // Avoid binding to a specific OrchestrationMetadata constructor signature; use reflection.
        var t = typeof(OrchestrationMetadata);

        object?[][] candidates =
        [
            new object?[] { instanceId },
            new object?[] { instanceId, string.Empty },
            new object?[] { instanceId, string.Empty, null },
            new object?[] { instanceId, string.Empty, null, null },
        ];

        foreach (var args in candidates)
        {
            var ctor = FindCtor(t, args);
            if (ctor is null) continue;

            var instance = ctor.Invoke(args);
            if (instance is OrchestrationMetadata md) return md;
        }

        var smallest = t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderBy(c => c.GetParameters().Length)
            .FirstOrDefault();

        if (smallest is null)
            throw new InvalidOperationException("No constructor found for OrchestrationMetadata.");

        var parms = smallest.GetParameters();
        var fallbackArgs = parms.Select(p => GetDefault(p.ParameterType, instanceId)).ToArray();
        var fallback = smallest.Invoke(fallbackArgs);

        return (OrchestrationMetadata)fallback;
    }

    private static ConstructorInfo? FindCtor(Type t, object?[] args)
    {
        foreach (var ctor in t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var ps = ctor.GetParameters();
            if (ps.Length != args.Length) continue;

            bool ok = true;
            for (int i = 0; i < ps.Length; i++)
            {
                var pt = ps[i].ParameterType;
                var av = args[i];

                if (av is null)
                {
                    if (pt.IsValueType && Nullable.GetUnderlyingType(pt) is null) { ok = false; break; }
                }
                else
                {
                    if (!pt.IsInstanceOfType(av)) { ok = false; break; }
                }
            }

            if (ok) return ctor;
        }
        return null;
    }

    private static object? GetDefault(Type t, string instanceId)
    {
        if (t == typeof(string)) return instanceId;
        if (t.IsValueType) return Activator.CreateInstance(t);
        return null;
    }
}
