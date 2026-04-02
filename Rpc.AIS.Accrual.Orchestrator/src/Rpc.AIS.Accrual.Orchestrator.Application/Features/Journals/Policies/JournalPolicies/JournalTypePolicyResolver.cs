using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.JournalPolicies;

public sealed class JournalTypePolicyResolver : IJournalTypePolicyResolver
{
    private readonly IReadOnlyDictionary<JournalType, IJournalTypePolicy> _policies;

    public JournalTypePolicyResolver(IEnumerable<IJournalTypePolicy> policies)
    {
        if (policies is null) throw new ArgumentNullException(nameof(policies));

        // Build a stable map. If duplicates exist, "last wins" to allow override policies in DI.
        _policies = policies
            .Where(p => p is not null)
            .GroupBy(p => p.JournalType)
            .ToDictionary(g => g.Key, g => g.Last());
    }

    public IJournalTypePolicy Resolve(JournalType journalType)
    {
        if (_policies.TryGetValue(journalType, out var policy) && policy is not null)
            return policy;

        // Never crash runtime due to missing DI registration.
        return new DefaultJournalTypePolicy(journalType);
    }

    /// <summary>
    /// Safe fallback policy used when no policy is registered for a journal type.
    /// Ensures SectionKey is valid so JSON building cannot crash.
    /// </summary>
    private sealed class DefaultJournalTypePolicy : IJournalTypePolicy
    {
        public JournalType JournalType { get; }

        public string SectionKey { get; }

        public DefaultJournalTypePolicy(JournalType journalType)
        {
            JournalType = journalType;
            SectionKey = journalType switch
            {
                JournalType.Item => "WOItemLines",
                JournalType.Expense => "WOExpLines",
                JournalType.Hour => "WOHourLines",
                _ => "WOUnknownLines"
            };
        }

        public void ValidateLocalLine(
            Guid workOrderGuid,
            string? workOrderId,
            Guid workOrderLineGuid,
            JsonElement line,
            List<WoPayloadValidationFailure> invalidFailures)
        {
            // No-op fallback.
            // Endpoint-level validation + WoLocalValidator handles required fields.
        }
    }
}
