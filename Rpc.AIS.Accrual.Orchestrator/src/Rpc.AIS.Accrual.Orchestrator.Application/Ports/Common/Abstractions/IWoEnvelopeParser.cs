using System.Text.Json;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

public interface IWoEnvelopeParser
{
    bool TryGetWoList(
        RunContext context,
        JournalType journalType,
        JsonElement root,
        out JsonElement woList,
        out WoPayloadValidationResult? failureResult);
}
