// File: .../Core/Domain/Delta/DeltaGrouping.cs
// Extracted from DeltaCalculationEngine.cs to improve SRP.


namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;

using System;
using System.Globalization;

internal static class DeltaGrouping
{
    internal static string BuildFsaSignature(FsaWorkOrderLineSnapshot fsa)
    {
        // :
        // Treat fields as "unknown/unchanged" when they were not present in the incoming FSA payload.
        // This prevents false ReverseAndRecreate decisions when FSA sends partial updates (e.g., only Quantity).
        var parts = new System.Collections.Generic.List<string>(5);

        if (fsa.DepartmentProvided)
            parts.Add($"D={(fsa.Department?.Trim() ?? string.Empty)}");

        if (fsa.ProductLineProvided)
            parts.Add($"PL={(fsa.ProductLine?.Trim() ?? string.Empty)}");

        if (fsa.WarehouseProvided)
            parts.Add($"W={(fsa.Warehouse?.Trim() ?? string.Empty)}");

        if (fsa.LinePropertyProvided)
            parts.Add($"LP={(fsa.LineProperty?.Trim() ?? string.Empty)}");

        if (fsa.UnitPriceProvided)
        {
            var price = fsa.CalculatedUnitPrice.HasValue
                ? fsa.CalculatedUnitPrice.Value.ToString("0.########", CultureInfo.InvariantCulture)
                : string.Empty;
            parts.Add($"P={price}");
        }

        return string.Join("|", parts);
    }


    internal static string Norm(string? s) => (s ?? string.Empty).Trim();

    internal static bool Eq(string a, string b)
        => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    internal static string NormSig(string? s)
        => (s ?? string.Empty).Replace(" ", string.Empty).Trim();

    internal static bool PriceEq(decimal? a, decimal? b)
    {
        if (!a.HasValue && !b.HasValue) return true;
        if (!a.HasValue || !b.HasValue) return false;

        var diff = Math.Abs(a.Value - b.Value);
        return diff <= AisConstants.Delta.PriceComparisonTolerance;
    }

    internal static bool TryParseSignature(
        string sig,
        out string dept,
        out string prod,
        out string wh,
        out string lp,
        out decimal? price)
    {
        dept = string.Empty;
        prod = string.Empty;
        wh = string.Empty;
        lp = string.Empty;
        price = null;

        if (string.IsNullOrWhiteSpace(sig))
            return false;

        var parts = sig.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        var sawAny = false;

        foreach (var part in parts)
        {
            var idx = part.IndexOf('=', StringComparison.Ordinal);
            if (idx <= 0 || idx >= part.Length - 1) continue;

            var k = part[..idx].Trim();
            var v = part[(idx + 1)..].Trim();
            if (k.Length == 0) continue;

            sawAny = true;

            if (k.Equals("D", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("Dept", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("Department", StringComparison.OrdinalIgnoreCase))
            {
                dept = v;
                continue;
            }

            if (k.Equals("PL", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("Prod", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("Product", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("ProductLine", StringComparison.OrdinalIgnoreCase))
            {
                prod = v;
                continue;
            }

            if (k.Equals("W", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("WH", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("Warehouse", StringComparison.OrdinalIgnoreCase))
            {
                wh = v;
                continue;
            }

            if (k.Equals("LP", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("LineProperty", StringComparison.OrdinalIgnoreCase))
            {
                lp = v;
                continue;
            }

            if (k.Equals("P", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("Price", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("UnitPrice", StringComparison.OrdinalIgnoreCase))
            {
                if (decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                    price = d;
                continue;
            }
        }

        dept = Norm(dept);
        prod = Norm(prod);
        wh = Norm(wh);
        lp = Norm(lp);

        return sawAny;
    }
}
