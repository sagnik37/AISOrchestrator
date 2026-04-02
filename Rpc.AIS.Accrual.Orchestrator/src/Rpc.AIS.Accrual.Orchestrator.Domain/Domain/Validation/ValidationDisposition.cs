using System;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

/// <summary>
/// Indicates how AIS should handle a validation issue.
/// </summary>
public enum ValidationDisposition
{
    /// <summary>
    /// Record is valid and may be posted.
    /// </summary>
    Valid = 0,

    /// <summary>
    /// Record is invalid (data/contract issue) and should not be retried.
    /// </summary>
    Invalid = 1,

    /// <summary>
    /// Record failed validation due to a transient dependency and may be retried.
    /// </summary>
    Retryable = 2,

    /// <summary>
    /// Validation cannot proceed due to a configuration or system issue; the run should stop.
    /// </summary>
    FailFast = 3
}
