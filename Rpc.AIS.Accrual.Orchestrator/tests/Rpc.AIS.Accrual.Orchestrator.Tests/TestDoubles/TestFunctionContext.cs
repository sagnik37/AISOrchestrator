using System;
using System.Collections;
using System.Collections.Generic;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Tests.TestDoubles;

/// <summary>
/// Provides test function context behavior.
/// </summary>
public sealed class TestFunctionContext : FunctionContext
{
    private readonly IServiceProvider _services;
    private readonly IInvocationFeatures _features = new SimpleInvocationFeatures();

    // ? Keeps  existing test calls working: new TestFunctionContext(serviceProvider)
    public TestFunctionContext(IServiceProvider services)
        : this(sc =>
        {
            // Merge in the already-built provider (as a fallback resolver)
            sc.AddSingleton(services);
        })
    {
    }

    // ? New flexible option: new TestFunctionContext(sc => { sc.AddSingleton(...); })
    public TestFunctionContext(Action<IServiceCollection>? configureServices = null)
    {
        var sc = new ServiceCollection();

        // : Many worker versions resolve serializer from WorkerOptions.Serializer
        sc.AddOptions<WorkerOptions>()
          .Configure(o => o.Serializer = new JsonObjectSerializer());

        // Also register ObjectSerializer directly (harmless, helps across versions)
        sc.AddSingleton<ObjectSerializer>(new JsonObjectSerializer());

        configureServices?.Invoke(sc);

        _services = new CompositeServiceProvider(
            primary: sc.BuildServiceProvider(),
            fallback: TryResolveFallback(configureServices));
    }

    /// <summary>
    /// Executes try resolve fallback.
    /// </summary>
    private static IServiceProvider? TryResolveFallback(Action<IServiceCollection>? configureServices)
    {
        // If caller provided an IServiceProvider via the IServiceCollection singleton,
        // CompositeServiceProvider can find it via primary.GetService<IServiceProvider>() at runtime.
        // We just return null here; CompositeServiceProvider will resolve it lazily.
        return null;
    }

    public override string InvocationId { get; } = Guid.NewGuid().ToString("N");
    public override string FunctionId { get; } = Guid.NewGuid().ToString("N");

    public override TraceContext TraceContext { get; } = null!;
    public override BindingContext BindingContext { get; } = null!;
    public override RetryContext RetryContext { get; } = null!;
    public override FunctionDefinition FunctionDefinition { get; } = null!;

    public override IServiceProvider InstanceServices
    {
        get => _services;
        set => throw new NotSupportedException("Setting InstanceServices is not supported in TestFunctionContext.");
    }

    public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();

    public override IInvocationFeatures Features => _features;

    // ------------------------------------------------------------
    // Minimal IInvocationFeatures (public API only)
    // ------------------------------------------------------------
    /// <summary>
    /// Provides simple invocation features behavior.
    /// </summary>
    private sealed class SimpleInvocationFeatures : IInvocationFeatures
    {
        private readonly Dictionary<Type, object?> _map = new();

        /// <summary>
        /// Executes get enumerator.
        /// </summary>
        public IEnumerator<KeyValuePair<Type, object>> GetEnumerator()
        {
            foreach (var kv in _map)
            {
                if (kv.Value is not null)
                    yield return new KeyValuePair<Type, object>(kv.Key, kv.Value);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public T Get<T>()
        {
            if (_map.TryGetValue(typeof(T), out var value) && value is not null)
                return (T)value;

            return default!;
        }

        public void Set<T>(T instance) => _map[typeof(T)] = instance;
    }

    // ------------------------------------------------------------
    // Composite service provider: resolves from primary first,
    // then (optionally) from a fallback IServiceProvider stored
    // as a singleton in primary ( existing ServiceProvider).
    // ------------------------------------------------------------
    /// <summary>
    /// Provides composite service provider behavior.
    /// </summary>
    private sealed class CompositeServiceProvider : IServiceProvider
    {
        private readonly IServiceProvider _primary;
        private readonly IServiceProvider? _fallback;

        public CompositeServiceProvider(IServiceProvider primary, IServiceProvider? fallback)
        {
            _primary = primary;
            _fallback = fallback;
        }

        /// <summary>
        /// Executes get service.
        /// </summary>
        public object? GetService(Type serviceType)
        {
            var fromPrimary = _primary.GetService(serviceType);
            if (fromPrimary is not null) return fromPrimary;

            // If caller passed an IServiceProvider as a singleton, use it as fallback
            var embeddedFallback = _primary.GetService(typeof(IServiceProvider)) as IServiceProvider;
            if (embeddedFallback is not null)
            {
                var fromEmbedded = embeddedFallback.GetService(serviceType);
                if (fromEmbedded is not null) return fromEmbedded;
            }

            return _fallback?.GetService(serviceType);
        }
    }
}
