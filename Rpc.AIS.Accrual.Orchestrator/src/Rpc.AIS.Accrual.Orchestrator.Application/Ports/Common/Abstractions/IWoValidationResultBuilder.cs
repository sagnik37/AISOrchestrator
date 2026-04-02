using System.Collections.Generic;
using System.Diagnostics;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

public interface IWoValidationResultBuilder
{
    WoPayloadValidationResult BuildResult(
        RunContext context,
        JournalType journalType,
        int workOrdersBefore,
        List<FilteredWorkOrder> validWorkOrders,
        List<FilteredWorkOrder> retryableWorkOrders,
        List<WoPayloadValidationFailure> invalidFailures,
        List<WoPayloadValidationFailure> retryableFailures,
        Stopwatch stopwatch);
}
