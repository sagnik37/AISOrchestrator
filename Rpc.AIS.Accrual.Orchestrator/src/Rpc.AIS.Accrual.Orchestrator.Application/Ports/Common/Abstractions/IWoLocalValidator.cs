using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

public interface IWoLocalValidator
{
    void ValidateLocally(
       FscmEndpointType endpoint,
       JournalType journalType,
       JsonElement woList,
       List<WoPayloadValidationFailure> invalidFailures,
       List<WoPayloadValidationFailure> retryableFailures,
       List<FilteredWorkOrder> validWorkOrders,
       List<FilteredWorkOrder> retryableWorkOrders,
       CancellationToken ct);
}
