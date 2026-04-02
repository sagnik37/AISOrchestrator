using System.Collections.Generic;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Utilities;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;

/// <summary>
/// Defines i fscm posting response parser behavior.
/// </summary>
public interface IFscmPostingResponseParser
{
    ParseOutcome Parse(string responseBody);
}

/// <summary>
/// Carries parse outcome data.
/// </summary>
public sealed record ParseOutcome(bool Ok, string? JournalId, string Message, IReadOnlyList<PostError> ParseErrors);

/// <summary>
/// Provides fscm posting response parser adapter behavior.
/// </summary>
public sealed class FscmPostingResponseParserAdapter : IFscmPostingResponseParser
{
    /// <summary>
    /// Executes parse.
    /// </summary>
    public ParseOutcome Parse(string responseBody)
    {
        // Infrastructure.Utilities.FscmPostingResponseParser.TryParse returns a tuple.
        var (ok, journalId, message, parseErrors) = FscmPostingResponseParser.TryParse(responseBody);

        // Treat tuple "ok" as the authoritative flag. Parsing failures are represented by parseErrors.
        return new ParseOutcome(ok, journalId, message ?? (ok ? "OK" : "Parse failed"), parseErrors ?? new List<PostError>());
    }
}
