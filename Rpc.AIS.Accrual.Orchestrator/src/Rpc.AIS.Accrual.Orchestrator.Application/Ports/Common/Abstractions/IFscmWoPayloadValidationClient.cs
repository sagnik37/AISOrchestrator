using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Calls FSCM custom validation endpoint for WO payloads.
/// Implemented in Infrastructure.
/// </summary>
public interface IFscmWoPayloadValidationClient
{
    Task<RemoteWoPayloadValidationResult> ValidateAsync(
        RunContext ctx,
        JournalType journalType,
        string normalizedWoPayloadJson,
        CancellationToken ct);
}

/// <summary>
/// Parsed outcome of remote validation call.
/// Kept in Core so Core can consume without referencing Infrastructure.
/// </summary>
public sealed record RemoteWoPayloadValidationResult(
    bool IsSuccessStatusCode,
    int StatusCode,
    string FilteredPayloadJson,
    IReadOnlyList<WoPayloadValidationFailure> Failures,
    string? RawResponse,
    long ElapsedMs,
    string Url);
