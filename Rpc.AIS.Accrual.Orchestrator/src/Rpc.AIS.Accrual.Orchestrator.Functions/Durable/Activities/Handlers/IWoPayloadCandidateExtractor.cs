using System.Collections.Generic;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services.Handlers;

public interface IWoPayloadCandidateExtractor
{
    IReadOnlyDictionary<JournalType, IReadOnlyList<WoPayloadCandidateWorkOrder>> ExtractByJournal(string woPayloadJson);
}
