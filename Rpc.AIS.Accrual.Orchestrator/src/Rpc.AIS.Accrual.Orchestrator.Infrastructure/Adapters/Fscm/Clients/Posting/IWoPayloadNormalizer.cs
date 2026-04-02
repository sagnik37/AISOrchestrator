using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;

/// <summary>
/// Normalizes incoming WO payload JSON to the canonical shape used by AIS:
/// { "_request": { "WOList": [ ... ] } }.
/// </summary>
public interface IWoPayloadNormalizer
{
    string NormalizeToWoListKey(string woPayloadJson);
}

/// <summary>
/// Provides wo payload normalizer behavior.
/// </summary>
public sealed class WoPayloadNormalizer : IWoPayloadNormalizer
{
    /// <summary>
    /// Executes normalize to wo list key.
    /// </summary>
    public string NormalizeToWoListKey(string woPayloadJson)
        => WoPayloadJsonToolkit.NormalizeWoPayloadToWoListKey(woPayloadJson);
}
