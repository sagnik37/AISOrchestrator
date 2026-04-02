namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

public static class LogScopeKeys
{
    public const string RunId = "RunId";
    public const string CorrelationId = "CorrelationId";
    public const string InstanceId = "InstanceId";
    public const string ParentInstanceId = "ParentInstanceId";
    public const string BatchId = "BatchId";

    public const string FlowName = "FlowName";
    public const string TriggerName = "TriggerName";
    public const string TriggerChannel = "TriggerChannel";
    public const string SourceSystem = "SourceSystem";
    public const string InitiatedBy = "InitiatedBy";

    public const string WorkOrderId = "WorkOrderId";
    public const string WorkOrderGuid = "WorkOrderGuid";
    public const string Company = "Company";
    public const string SubProjectId = "SubProjectId";

    public const string Stage = "Stage";
    public const string Outcome = "Outcome";
    public const string JournalType = "JournalType";

    public const string FailureCategory = "FailureCategory";
    public const string ErrorType = "ErrorType";
    public const string IsRetryable = "IsRetryable";
    public const string RetryAttempt = "RetryAttempt";

    public const string WorkOrderCount = "WorkOrderCount";
    public const string TotalOpenWorkOrders = "TotalOpenWorkOrders";
    public const string ProcessedCount = "ProcessedCount";
    public const string SucceededCount = "SucceededCount";
    public const string FailedCount = "FailedCount";
    public const string RemainingCount = "RemainingCount";

    public const string PayloadBytes = "PayloadBytes";
    public const string CandidateLineCount = "CandidateLineCount";
    public const string PostedLineCount = "PostedLineCount";
    public const string RetryableCount = "RetryableCount";
    public const string ErrorCount = "ErrorCount";
}
