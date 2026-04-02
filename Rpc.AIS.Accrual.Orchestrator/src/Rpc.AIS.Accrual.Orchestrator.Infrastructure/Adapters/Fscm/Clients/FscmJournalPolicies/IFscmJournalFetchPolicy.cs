namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.FscmJournalPolicies;

using System.Text.Json;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

/// <summary>
/// Journal-type specific FSCM OData metadata and mapping rules for journal history fetch.
/// OCP: eliminates journal-type switches in <see cref="Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.FscmJournalFetchHttpClient"/>.
/// </summary>
public interface IFscmJournalFetchPolicy
{
    JournalType JournalType { get; }

    /// <summary>FSCM OData entity set (e.g., JournalTrans).</summary>
    string EntitySet { get; }

    /// <summary>Comma-separated $select list.</summary>
    string Select { get; }

    /// <summary>Extracts the quantity/hours for this journal type. Must return a non-null value (0 if missing).</summary>
    decimal GetQuantity(JsonElement row);

    /// <summary>Extracts the unit price for this journal type (nullable).</summary>
    decimal? GetUnitPrice(JsonElement row);
}
