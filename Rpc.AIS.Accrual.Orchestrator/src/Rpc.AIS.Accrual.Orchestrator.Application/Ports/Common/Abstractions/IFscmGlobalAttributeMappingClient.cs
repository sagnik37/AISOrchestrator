using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Provides an in-memory mapping between FSA attribute keys and FSCM attribute names.
/// Source of truth is the FSCM OData entity set AttributeTypeGlobalAttributes (fields: Name, FSA).
/// </summary>
public interface IFscmGlobalAttributeMappingClient
{
    /// <summary>
    /// Returns a mapping dictionary where the key is the FSA attribute key (AttributeTypeGlobalAttributes.FSA)
    /// and the value is the FSCM attribute name (AttributeTypeGlobalAttributes.Name).
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetFsToFscmNameMapAsync(
        RunContext ctx,
        CancellationToken ct);
}
