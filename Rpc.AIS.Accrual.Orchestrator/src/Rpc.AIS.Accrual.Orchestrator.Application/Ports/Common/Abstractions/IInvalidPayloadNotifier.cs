using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Notifies about invalid work orders/lines found during AIS-side payload validation.
/// </summary>
public interface IInvalidPayloadNotifier
{
    Task NotifyAsync(
        RunContext context,
        JournalType journalType,
        IReadOnlyList<WoPayloadValidationFailure> failures,
        int workOrdersBefore,
        int workOrdersAfter,
        CancellationToken ct);
}
