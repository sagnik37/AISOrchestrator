using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services;

internal sealed partial class DeltaJournalSectionBuilder
{
    private static string? ResolveFsDescription(JsonObject lineObj)
    {
        // Preferred order of FS description fields.
        // Blank is a valid business update, so do not apply fallback logic here.
        return GetStringLooseAny(
            lineObj,
            "FSACustomerProductDesc",
            "msdyn_description",
            "JournalLineDescription");
    }

    private static string? ResolveFsCustomerProductId(JsonObject lineObj)
    {
        return GetStringLooseAny(
            lineObj,
            "FSACustomerProductID",
            "rpc_customerproductid",
            "RPCCustomerProductReference");
    }

    private static string? ResolveFsTaxability(JsonObject lineObj)
    {
        return GetStringLooseAny(
            lineObj,
            "FSATaxabilityType",
            "Taxability Type");
    }

    private static bool TryGetYesNoFromDataverseBoolean(JsonObject obj, string logicalName, out string yesNo)
    {
        yesNo = "No";

        // If formatted value exists, trust it (e.g. "Yes"/"No")
        if (JsonLooseKey.TryGetStringLoose(obj, logicalName + "@OData.Community.Display.V1.FormattedValue", out var formatted)
            && !string.IsNullOrWhiteSpace(formatted))
        {
            yesNo = formatted.Trim();
            return true;
        }

        if (!JsonLooseKey.TryGetNodeLoose(obj, logicalName, out var node) || node is null)
            return false;

        if (node is JsonValue v)
        {
            if (v.TryGetValue<bool>(out var b))
            {
                yesNo = b ? "Yes" : "No";
                return true;
            }

            if (v.TryGetValue<int>(out var i))
            {
                yesNo = i != 0 ? "Yes" : "No";
                return true;
            }

            if (v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
            {
                if (bool.TryParse(s, out var bb))
                {
                    yesNo = bb ? "Yes" : "No";
                    return true;
                }
                if (int.TryParse(s, out var ii))
                {
                    yesNo = ii != 0 ? "Yes" : "No";
                    return true;
                }
            }
        }

        return false;
    }

    private static decimal? ResolveFscmUnitPriceForReversal(FscmWorkOrderLineAggregation fscmAgg)
    {
        if (fscmAgg is null) return null;

        // Prefer effective unit price if aggregator provides it.
        if (fscmAgg.EffectiveUnitPrice.HasValue)
            return Math.Abs(fscmAgg.EffectiveUnitPrice.Value);

        // Fall back to extended amount / quantity when available.
        if (fscmAgg.TotalExtendedAmount.HasValue && fscmAgg.TotalQuantity != 0)
        {
            var price = fscmAgg.TotalExtendedAmount.Value / fscmAgg.TotalQuantity;
            return Math.Abs(price);
        }

        // As a last resort, try the first dimension bucket price.
        if (fscmAgg.DimensionBuckets is { Count: > 0 })
        {
            var p = fscmAgg.DimensionBuckets[0].CalculatedUnitPrice;
            if (p.HasValue) return Math.Abs(p.Value);
        }

        return null;
    }

    private static class Keys
    {
        public const string JournalLines = "JournalLines";
        public const string JournalDescription = "JournalDescription";
        public const string WorkOrderLineGuid = "WorkOrderLineGuid";

        public const string Quantity = "Quantity";
        public const string Duration = "Duration";

        public const string UnitCost = "UnitCost";
        public const string UnitAmount = "UnitAmount";

        // Explicit unit price signal from FSA (Intent-2). Do NOT infer explicitness from UnitCost/UnitAmount.
        public const string FsaUnitPrice = "FSAUnitPrice";

        public const string LineProperty = "LineProperty";
        public const string Warehouse = "Warehouse";
        public const string DimDepartment = "DimensionDepartment";
        public const string DimProduct = "DimensionProduct";

        public const string LineType = "LineType";

        public const string TransactionDate = "TransactionDate";

        // NEW canonical field for ops date (working date)
        public const string RpcWorkingDate = "RPCWorkingDate";

        // Source field name coming from Dataverse payload (if present)
        public const string RpcOperationsDate = "rpc_operationsdate";

        // Do not emit these in DELTA payload
        public const string ModifiedOn = "modifiedon";
        public const string RpcAccrualFieldModifiedOn = "rpc_accrualfieldmodifiedon";

        // Remove from payload (hygiene)
        public const string LineNum = "Line num";
    }

    private static DateTime ResolveWorkingDateUtcFromLineOrFallback(JsonObject lineObj, DateTime fallbackUtcDate)
    {
        // Prefer existing RPCWorkingDate if upstream already set it as FSCM literal
        if (JsonLooseKey.TryGetStringLoose(lineObj, Keys.RpcWorkingDate, out var rpcWorkingLiteral) &&
            !string.IsNullOrWhiteSpace(rpcWorkingLiteral) &&
            TryParseFscmDateLiteral(rpcWorkingLiteral!, out var rpcWorkingUtc))
        {
            return rpcWorkingUtc.Date;
        }

        // Prefer OperationDate if present (FS sends /Date(ms)/)
        if (JsonLooseKey.TryGetStringLoose(lineObj, "OperationDate", out var opLiteral) &&
            !string.IsNullOrWhiteSpace(opLiteral) &&
            TryParseFscmDateLiteral(opLiteral!, out var opUtc))
        {
            return opUtc.Date;
        }

        // Else try TransactionDate if present (/Date(ms)/)
        if (JsonLooseKey.TryGetStringLoose(lineObj, Keys.TransactionDate, out var txLiteral) &&
            !string.IsNullOrWhiteSpace(txLiteral) &&
            TryParseFscmDateLiteral(txLiteral!, out var txUtc))
        {
            return txUtc.Date;
        }

        // Else try Dataverse ISO string in rpc_operationsdate
        if (JsonLooseKey.TryGetNodeLoose(lineObj, Keys.RpcOperationsDate, out var opsNode) && opsNode is not null)
        {
            var parsed = TryParseIsoUtc(opsNode.ToString());
            if (parsed.HasValue) return parsed.Value.Date;

            var s = opsNode.ToString();
            if (!string.IsNullOrWhiteSpace(s) && TryParseFscmDateLiteral(s, out var dt))
                return dt.Date;
        }

        return fallbackUtcDate.Date;
    }

    private static DateTime? TryParseIsoUtc(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;

        if (DateTime.TryParse(
                s,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var dt))
        {
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }

        return null;
    }

    private static bool TryParseFscmDateLiteral(string literal, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(literal)) return false;

        var s = literal.Trim();
        if (!s.StartsWith("/Date(", StringComparison.OrdinalIgnoreCase)) return false;
        var end = s.IndexOf(")/", StringComparison.OrdinalIgnoreCase);
        if (end < 0) return false;

        var numPart = s.Substring(6, end - 6);
        if (!long.TryParse(numPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
            return false;

        utc = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        return true;
    }

    private static string ToFscmDateLiteral(DateTime utcDate)
    {
        var dt = new DateTime(utcDate.Year, utcDate.Month, utcDate.Day, 0, 0, 0, DateTimeKind.Utc);
        var ms = new DateTimeOffset(dt).ToUnixTimeMilliseconds();
        return $"/Date({ms})/";
    }

    private static bool HasAnyNodeLoose(JsonObject obj, params string[] keys)
        => keys.Any(k => JsonLooseKey.TryGetNodeLoose(obj, k, out var n) && n is not null);

    private static JsonObject? FindFirstObjectLoose(JsonObject root, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (JsonLooseKey.TryGetNodeLoose(root, k, out var node) && node is JsonObject o)
                return o;
        }

        return null;
    }

    private static JsonArray? FindJournalLinesArrayLoose(JsonObject section)
    {
        if (JsonLooseKey.TryGetNodeLoose(section, Keys.JournalLines, out var node) && node is JsonArray a)
            return a;

        if (JsonLooseKey.TryGetNodeLoose(section, "Journal lines", out var node2) && node2 is JsonArray a2)
            return a2;

        return null;
    }

    private static Guid GetWorkOrderLineGuid(JsonObject lineObj)
    {
        if (!JsonLooseKey.TryGetStringLoose(lineObj, Keys.WorkOrderLineGuid, out var s) || string.IsNullOrWhiteSpace(s))
            return Guid.Empty;

        s = s.Trim();
        if (s.StartsWith("{", StringComparison.Ordinal) && s.EndsWith("}", StringComparison.Ordinal))
            s = s.Substring(1, s.Length - 2);

        return Guid.TryParse(s, out var g) ? g : Guid.Empty;
    }

    private static decimal? GetDecimalLoose(JsonObject lineObj, string key)
    {
        if (JsonLooseKey.TryGetNodeLoose(lineObj, key, out var node) && node is not null)
            return TryParseDecimal(node);

        return null;
    }

    private static decimal? GetDecimalLooseAny(JsonObject lineObj, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (JsonLooseKey.TryGetNodeLoose(lineObj, k, out var node) && node is not null)
            {
                var d = TryParseDecimal(node);
                if (d.HasValue) return d;
            }
        }

        return null;
    }

    private static string? GetStringLooseAny(JsonObject lineObj, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (JsonLooseKey.TryGetStringLoose(lineObj, k, out var s) && !string.IsNullOrWhiteSpace(s))
                return s;
        }

        return null;
    }

    private static bool GetIsActive(JsonObject lineObj)
    {
        if (JsonLooseKey.TryGetNodeLoose(lineObj, "IsActive", out var node) && node is JsonValue v)
        {
            if (v.TryGetValue<bool>(out var b)) return b;
            if (v.TryGetValue<int>(out var i)) return i != 0;

            if (v.TryGetValue<string>(out var s))
            {
                if (bool.TryParse(s, out var bb)) return bb;
                if (int.TryParse(s, out var ii)) return ii != 0;
            }
        }

        if (JsonLooseKey.TryGetNodeLoose(lineObj, "statecode", out var stateNode) && stateNode is JsonValue sv)
        {
            if (sv.TryGetValue<int>(out var state)) return state == 0;
            if (sv.TryGetValue<string>(out var ss) && int.TryParse(ss, out var stateFromString))
                return stateFromString == 0;
        }

        if (TryGetYesNoFromDataverseBoolean(lineObj, "isactive", out var yn))
            return string.Equals(yn, "Yes", StringComparison.OrdinalIgnoreCase);

        // keep backward compatibility for older payloads
        return true;
    }

    private static decimal? TryParseDecimal(JsonNode node)
    {
        try
        {
            var s = node.ToString();
            if (string.IsNullOrWhiteSpace(s)) return null;

            if (decimal.TryParse(
                    s,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var d))
            {
                return d;
            }
        }
        catch
        {
        }

        return null;
    }

    private static void CopyIfPresentLoose(JsonObject src, JsonObject dst, string key)
    {
        if (JsonLooseKey.TryGetNodeLoose(src, key, out var node) && node is not null)
            dst[key] = node.DeepClone();
    }

    private static void RemoveLoose(JsonObject obj, string key)
    {
        if (obj.ContainsKey(key))
            obj.Remove(key);

        var match = obj.FirstOrDefault(kvp => string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(match.Key) && obj.ContainsKey(match.Key))
            obj.Remove(match.Key);
    }

    private static void SetOrAddLoose(JsonObject obj, string key, JsonNode value)
    {
        obj[key] = value;
    }

    private static void SetOrAddLoose(JsonObject obj, string key, string value)
    {
        obj[key] = value;
    }

    private static void SetOrAddLoose(JsonObject obj, string key, decimal value)
    {
        obj[key] = value;
    }
}
