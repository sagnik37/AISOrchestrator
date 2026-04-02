using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;

/// <summary>
/// DTOs for the FSCM posting endpoint:
/// /api/services/RPCJournalPostingWOIntServGrp/RPCJournalPostingWOIntServ/postJournal
///
/// Contract:
/// {
///   "_request": {
///     "JournalList": [ { "Company": "...", "JournalId": "...", "Type": "Hour|Item|Expense" }, ... ]
///   }
/// }
/// </summary>
public sealed record FscmPostJournalRequest(FscmPostJournalEnvelope _request);

public sealed record FscmPostJournalEnvelope(IReadOnlyList<FscmJournalPostItem> JournalList);

public sealed record FscmJournalPostItem(
    [property: JsonPropertyName("company")] string Company,
    [property: JsonPropertyName("journalId")] string JournalId,
    [property: JsonPropertyName("journalType")] string JournalType);
