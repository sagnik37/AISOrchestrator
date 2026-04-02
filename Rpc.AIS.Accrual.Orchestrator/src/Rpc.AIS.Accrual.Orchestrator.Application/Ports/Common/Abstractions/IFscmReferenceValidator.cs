using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

public interface IFscmReferenceValidator
{
    Task ApplyFscmCustomValidationAsync(
        RunContext context,
        JournalType journalType,
        List<WoPayloadValidationFailure> invalidFailures,
        List<WoPayloadValidationFailure> retryableFailures,
        List<FilteredWorkOrder> validWorkOrders,
        List<FilteredWorkOrder> retryableWorkOrders,
        Stopwatch stopwatch,
        CancellationToken ct);
}
