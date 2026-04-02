namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain;

/// <summary>
/// Carries accrual staging ref data.
/// </summary>
public sealed record AccrualStagingRef(
    string StagingId,
    JournalType JournalType,
    string SourceKey);
