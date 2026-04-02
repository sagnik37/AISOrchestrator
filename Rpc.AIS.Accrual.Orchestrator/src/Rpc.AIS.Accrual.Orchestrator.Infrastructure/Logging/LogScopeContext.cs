using System;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

/// <summary>
/// Normalized logging scope fields for all Functions/Activities/Clients.
/// Keep it small and stable; add fields only when they are broadly useful.
/// </summary>
public readonly record struct LogScopeContext
{
    // Required-ish (most scopes have these)
    public string? Function { get; init; }
    public string? Activity { get; init; }
    public string? Operation { get; init; }   // e.g., "PostJob", "CustomerChange"
    public string? Trigger { get; init; }     // legacy / compatibility
    public string? FlowName { get; init; }
    public string? TriggerName { get; init; }
    public string? TriggerChannel { get; init; }
    public string? InitiatedBy { get; init; }

    // Optional pipeline step name (e.g., "ScheduleDurableOrchestrator", "FetchFromFsa", "ValidateAndPost").
    public string? Step { get; init; }
    public string? Stage { get; init; }
    public string? Outcome { get; init; }
    public string? FailureCategory { get; init; }
    public string? ErrorType { get; init; }
    public bool? IsRetryable { get; init; }
    public int? RetryAttempt { get; init; }

    public string? RunId { get; init; }
    public string? CorrelationId { get; init; }
    public string? SourceSystem { get; init; }
    public string? BatchId { get; init; }
    public string? Company { get; init; }

    // Optional identifiers
    public Guid? WorkOrderGuid { get; init; }
    public string? WorkOrderId { get; init; }
    public string? SubProjectId { get; init; }
    public string? DurableInstanceId { get; init; }
    public string? ParentInstanceId { get; init; }

    // Optional domain signal
    public JournalType? JournalType { get; init; }
    public int? WorkOrderCount { get; init; }
    public int? TotalOpenWorkOrders { get; init; }
    public int? ProcessedCount { get; init; }
    public int? SucceededCount { get; init; }
    public int? FailedCount { get; init; }
    public int? RemainingCount { get; init; }


    // Convenience factories
    public static LogScopeContext ForHttp(string function, string runId, string correlationId, string sourceSystem) =>
        new()
        {
            Function = function,
            Operation = function,
            FlowName = function,
            Trigger = "Http",
            TriggerName = function,
            TriggerChannel = "Http",
            InitiatedBy = sourceSystem,
            RunId = runId,
            CorrelationId = correlationId,
            SourceSystem = sourceSystem
        };

    public static LogScopeContext ForTimer(string function, string runId, string correlationId, string mode) =>
        new()
        {
            Function = function,
            Operation = function,
            FlowName = function,
            Trigger = "Timer",
            TriggerName = function,
            TriggerChannel = "Timer",
            InitiatedBy = "Timer",
            RunId = runId,
            CorrelationId = correlationId,
            SourceSystem = "Timer"
        };

    public LogScopeContext WithWorkOrder(Guid woGuid) => this with { WorkOrderGuid = woGuid };
    public LogScopeContext WithSubProject(string subProjectId) => this with { SubProjectId = subProjectId };
    public LogScopeContext WithDurableInstance(string instanceId) => this with { DurableInstanceId = instanceId };
    public LogScopeContext WithStep(string step) => this with { Step = step };
    public LogScopeContext WithStage(string stage) => this with { Stage = stage };
    public LogScopeContext WithOutcome(string outcome) => this with { Outcome = outcome };
    public LogScopeContext WithBatch(string batchId) => this with { BatchId = batchId };
    public LogScopeContext WithCompany(string company) => this with { Company = company };
    public LogScopeContext WithJournal(JournalType jt) => this with { JournalType = jt };
}
