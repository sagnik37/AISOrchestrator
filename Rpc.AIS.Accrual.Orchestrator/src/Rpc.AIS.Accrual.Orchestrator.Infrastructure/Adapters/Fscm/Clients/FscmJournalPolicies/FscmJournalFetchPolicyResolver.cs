namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.FscmJournalPolicies;

using System;
using System.Collections.Generic;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

/// <summary>
/// Resolves the configured <see cref="IFscmJournalFetchPolicy"/> for a given <see cref="JournalType"/>.
/// </summary>
public sealed class FscmJournalFetchPolicyResolver
{
    private readonly IReadOnlyDictionary<JournalType, IFscmJournalFetchPolicy> _map;

    public FscmJournalFetchPolicyResolver(IEnumerable<IFscmJournalFetchPolicy> policies)
    {
        if (policies is null) throw new ArgumentNullException(nameof(policies));

        var map = new Dictionary<JournalType, IFscmJournalFetchPolicy>();
        foreach (var p in policies)
        {
            if (map.ContainsKey(p.JournalType))
                throw new InvalidOperationException($"Duplicate FSCM journal fetch policy registration for {p.JournalType}.");
            map[p.JournalType] = p;
        }

        _map = map;
    }

    public IFscmJournalFetchPolicy Resolve(JournalType journalType)
    {
        if (_map.TryGetValue(journalType, out var p))
            return p;

        throw new KeyNotFoundException($"No FSCM journal fetch policy registered for {journalType}.");
    }
}
