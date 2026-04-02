namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.Enrichment;

using System.Collections.Generic;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

internal interface IJournalNamesInjector
{
    string InjectJournalNamesIntoPayload(
        string payloadJson,
        IReadOnlyDictionary<string, LegalEntityJournalNames> journalNamesByCompany);
}
