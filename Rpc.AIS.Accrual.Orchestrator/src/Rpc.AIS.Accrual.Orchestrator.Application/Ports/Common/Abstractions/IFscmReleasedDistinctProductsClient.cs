using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Fetches project category identifiers from FSCM CDSReleasedDistinctProducts for a set of ItemNumbers.
/// This is the ONLY source of ProjCategoryId/RPCProjCategoryId (Dataverse enrichment is intentionally not used).
/// </summary>
public interface IFscmReleasedDistinctProductsClient
{
    Task<IReadOnlyDictionary<string, ReleasedDistinctProductCategory>> GetCategoriesByItemNumberAsync(
        RunContext ctx,
        IReadOnlyList<string> itemNumbers,
        CancellationToken ct);
}

/// <summary>
/// FSCM category ids for a released distinct product.
/// </summary>
public sealed record ReleasedDistinctProductCategory(
    string ItemNumber,
    string? ProjCategoryId,
    string? RpcProjCategoryId);
