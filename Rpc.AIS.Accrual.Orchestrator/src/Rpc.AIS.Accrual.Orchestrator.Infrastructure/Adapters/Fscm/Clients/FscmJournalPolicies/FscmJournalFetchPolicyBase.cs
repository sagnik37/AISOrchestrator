namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.FscmJournalPolicies;

using System;
using System.Globalization;
using System.Text.Json;

/// <summary>
/// Base helpers for FSCM journal fetch policies.
/// </summary>
public abstract class FscmJournalFetchPolicyBase : IFscmJournalFetchPolicy
{
    public abstract Core.Domain.JournalType JournalType { get; }
    public abstract string EntitySet { get; }
    public abstract string Select { get; }

    /// <summary>
    /// Optional fallback $select used when the primary Select fails due to missing fields in a given FSCM environment.
    /// Defaults to <see cref="Select"/>.
    /// </summary>
    public virtual string SelectFallback => Select;

    public abstract decimal GetQuantity(JsonElement row);
    public abstract decimal? GetUnitPrice(JsonElement row);

    protected static decimal? TryGetDecimal(JsonElement obj, string propName)
    {
        if (!obj.TryGetProperty(propName, out var p))
            return null;

        return p.ValueKind switch
        {
            JsonValueKind.Number when p.TryGetDecimal(out var d) => d,
            JsonValueKind.String => TryParseDecimal(p.GetString()),
            _ => null
        };

        static decimal? TryParseDecimal(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
        }
    }
}
