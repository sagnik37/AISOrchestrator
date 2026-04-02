namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain;

/// <summary>
/// Carries run context data.
/// </summary>
public sealed record RunContext(
    string RunId,
    DateTimeOffset StartedAtUtc,
    string? TriggeredBy,
    string CorrelationId,
    string? SourceSystem = null,
    string? DataAreaId = null);
