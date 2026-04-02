using System.Collections.Generic;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.FscmJournalPolicies;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

public interface IFscmJournalLineRowMapper
{
    IReadOnlyList<FscmJournalLine> MapMany(string json, IFscmJournalFetchPolicy policy, string workOrderLineIdField);
}
