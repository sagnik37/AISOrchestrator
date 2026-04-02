using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services;

public static partial class DeltaPayloadBuilder
{
    private static IReadOnlyList<TLine>? GetLines<TLine>(object obj, params string[] names) where TLine : class
    {
        foreach (var n in names)
        {
            var p = obj.GetType().GetProperty(n, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p is null) continue;

            var v = p.GetValue(obj);
            if (v is null) continue;

            if (v is IReadOnlyList<TLine> ro) return ro;
            if (v is List<TLine> l) return l;
            if (v is IEnumerable<TLine> e) return e.ToList();
        }

        return null;
    }

    private static decimal? GetDecimalProp(object obj, params string[] names)
    {
        foreach (var n in names)
        {
            var p = obj.GetType().GetProperty(n, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p is null) continue;

            var v = p.GetValue(obj);
            if (v is null) continue;

            if (v is decimal d) return d;

            if (decimal.TryParse(
                    v.ToString(),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string? GetStringProp(object obj, params string[] names)
    {
        foreach (var n in names)
        {
            var p = obj.GetType().GetProperty(n, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p is null) continue;

            var v = p.GetValue(obj);
            if (v is null) continue;

            return v.ToString();
        }

        return null;
    }

    private static Guid GetGuidProp(object obj, params string[] names)
    {
        foreach (var n in names)
        {
            var p = obj.GetType().GetProperty(n, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p is null) continue;

            var v = p.GetValue(obj);
            if (v is null) continue;

            if (v is Guid g && g != Guid.Empty) return g;

            if (Guid.TryParse(v.ToString(), out var parsed) && parsed != Guid.Empty)
                return parsed;
        }

        return Guid.Empty;
    }

    private static DateTime? GetDateTimeProp(object obj, params string[] names)
    {
        foreach (var n in names)
        {
            var p = obj.GetType().GetProperty(n, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p is null) continue;

            var v = p.GetValue(obj);
            if (v is null) continue;

            if (v is DateTime dt) return dt;

            if (DateTime.TryParse(
                    v.ToString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }
        }

        return null;
    }
}
