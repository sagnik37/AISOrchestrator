// File: src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Clients/FscmAccountingPeriodHttpClient.cs
// FscmAccountingPeriodHttpClient.cs
// Thin facade that preserves IFscmAccountingPeriodClient contract, delegating logic to FscmAccountingPeriodResolver.
// Behavior preserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Thin facade that preserves <see cref="IFscmAccountingPeriodClient"/> contract,
/// delegating all logic to <see cref="FscmAccountingPeriodResolver"/>.
/// </summary>
public sealed class FscmAccountingPeriodHttpClient : IFscmAccountingPeriodClient
{
    private readonly FscmAccountingPeriodResolver _resolver;

    public FscmAccountingPeriodHttpClient(
        HttpClient http,
        FscmOptions endpoints,
        ILogger<FscmAccountingPeriodHttpClient> logger,
        ILogger<FscmAccountingPeriodResolver> resolverLogger)
    {
        if (http is null) throw new ArgumentNullException(nameof(http));
        if (endpoints is null) throw new ArgumentNullException(nameof(endpoints));
        if (logger is null) throw new ArgumentNullException(nameof(logger));
        if (resolverLogger is null) throw new ArgumentNullException(nameof(resolverLogger));

        _resolver = new FscmAccountingPeriodResolver(http, endpoints, resolverLogger);
    }

    public Task<AccountingPeriodSnapshot> GetSnapshotAsync(RunContext context, CancellationToken ct)
        => _resolver.GetSnapshotAsync(context, ct);
}
