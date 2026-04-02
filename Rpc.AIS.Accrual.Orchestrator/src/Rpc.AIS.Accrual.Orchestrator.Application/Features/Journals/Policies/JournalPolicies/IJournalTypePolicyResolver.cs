namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.JournalPolicies;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

/// <summary>
/// Resolves the configured <see cref="IJournalTypePolicy"/> for a given <see cref="JournalType"/>.
/// Exposed to allow other layers (e.g., Infrastructure) to reuse the same journal metadata (section keys),
/// avoiding journal-type switches.
/// </summary>
public interface IJournalTypePolicyResolver
{
    IJournalTypePolicy Resolve(JournalType journalType);
}
