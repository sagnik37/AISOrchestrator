using System;
using System.Collections.Generic;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Options;

/// <summary>
/// Provides mapping from FSA (Dataverse) attribute logical names to FSCM invoice attribute names.
/// Loaded once at the beginning of each execution (app configuration / environment variables).
/// </summary>
public sealed class InvoiceAttributeMappingOptions
{
    /// <summary>
    /// Key: FSA attribute logical name (e.g., rpc_wellname)
    /// Value: FSCM invoice attribute name to use in the outbound payload.
    /// </summary>
    public Dictionary<string, string> FsToFscm { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
