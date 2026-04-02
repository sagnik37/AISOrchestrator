using System;
using System.Collections.Generic;
using System.Linq;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

/// <summary>
/// Outcome of AIS-side payload validation.
/// </summary>
public sealed class WoPayloadValidationResult
{
    public WoPayloadValidationResult(
        string filteredPayloadJson,
        IReadOnlyList<WoPayloadValidationFailure> failures,
        int workOrdersBefore,
        int workOrdersAfter)
        : this(filteredPayloadJson, failures, workOrdersBefore, workOrdersAfter, retryablePayloadJson: "{}", retryableFailures: Array.Empty<WoPayloadValidationFailure>(), retryableWorkOrdersAfter: 0)
    {
    }

    public WoPayloadValidationResult(
        string filteredPayloadJson,
        IReadOnlyList<WoPayloadValidationFailure> failures,
        int workOrdersBefore,
        int workOrdersAfter,
        string retryablePayloadJson,
        IReadOnlyList<WoPayloadValidationFailure> retryableFailures,
        int retryableWorkOrdersAfter)
    {
        FilteredPayloadJson = filteredPayloadJson ?? throw new ArgumentNullException(nameof(filteredPayloadJson));
        Failures = failures ?? Array.Empty<WoPayloadValidationFailure>();
        WorkOrdersBefore = workOrdersBefore;
        WorkOrdersAfter = workOrdersAfter;

        RetryablePayloadJson = retryablePayloadJson ?? "{}";
        RetryableFailures = retryableFailures ?? Array.Empty<WoPayloadValidationFailure>();
        RetryableWorkOrdersAfter = retryableWorkOrdersAfter;
    }

    public string FilteredPayloadJson { get; }
    public IReadOnlyList<WoPayloadValidationFailure> Failures { get; }
    public int WorkOrdersBefore { get; }
    public int WorkOrdersAfter { get; }

    public string RetryablePayloadJson { get; }
    public IReadOnlyList<WoPayloadValidationFailure> RetryableFailures { get; }
    public int RetryableWorkOrdersAfter { get; }

    public bool HasFailures => Failures.Count > 0;
    public bool HasRetryables => RetryableWorkOrdersAfter > 0 || RetryableFailures.Count > 0;

    public bool HasFailFast => Failures.Any(f => f.Disposition == ValidationDisposition.FailFast) ||
                              RetryableFailures.Any(f => f.Disposition == ValidationDisposition.FailFast);
}
