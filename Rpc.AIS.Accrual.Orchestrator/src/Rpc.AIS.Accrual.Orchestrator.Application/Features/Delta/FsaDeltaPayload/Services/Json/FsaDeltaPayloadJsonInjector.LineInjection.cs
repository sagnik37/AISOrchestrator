using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

using static Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.FsaDeltaPayloadJsonUtil;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload;

internal static partial class FsaDeltaPayloadJsonInjector
{
    private const string MissingDateSentinel = "/Date(-2208988800000)/";

    private static bool CopyJournalLineWithInjectionAndStats(
        JsonElement line,
        Utf8JsonWriter w,
        Dictionary<Guid, FsLineExtras> extrasByLineGuid,
        WoEnrichmentStats s)
    {
        Guid? lineGuid = TryReadLineGuid(line);

        var hasExtras = false;
        FsLineExtras extras = default!;

        if (lineGuid.HasValue && extrasByLineGuid.TryGetValue(lineGuid.Value, out var found))
        {
            extras = found;
            hasExtras = true;
        }

        var opsRaw = TryGetString(line, "rpc_operationsdate")
                  ?? TryGetString(line, "rpc_OperationsDate");

        var anyFilled = false;

        if (string.IsNullOrWhiteSpace(opsRaw) && hasExtras && !string.IsNullOrWhiteSpace(extras.OperationsDate))
        {
            opsRaw = extras.OperationsDate;
            anyFilled = true;
            s.MarkFilledOperationsDate();
        }

        var opsLiteral = NormalizeToFscmDateLiteralOrNull(opsRaw);

        var hasRpcWorkingDate = false;
        var hasTransactionDate = false;
        var hasOperationDate = false;

        w.WriteStartObject();

        foreach (var p in line.EnumerateObject())
        {
            if (p.NameEquals("rpc_operationsdate") || p.NameEquals("rpc_OperationsDate"))
                continue;

            if (p.NameEquals("RPCWorkingDate"))
            {
                hasRpcWorkingDate = true;
                w.WritePropertyName("RPCWorkingDate");
                WritePreferExistingElseEnrichedDate(p.Value, opsLiteral, w);
                continue;
            }

            if (p.NameEquals("TransactionDate"))
            {
                hasTransactionDate = true;
                w.WritePropertyName("TransactionDate");
                WritePreferExistingElseEnrichedDate(p.Value, opsLiteral, w);
                continue;
            }

            if (p.NameEquals("OperationDate"))
            {
                hasOperationDate = true;
                w.WritePropertyName("OperationDate");
                WritePreferExistingElseEnrichedDate(p.Value, opsLiteral, w);
                continue;
            }

            w.WritePropertyName(p.Name);
            p.Value.WriteTo(w);
        }

        WriteMissingCanonicalDates(hasRpcWorkingDate, hasTransactionDate, hasOperationDate, opsLiteral, w);

        w.WriteEndObject();
        return anyFilled;
    }

    private static Guid? TryReadLineGuid(JsonElement line)
    {
        if (line.TryGetProperty("WorkOrderLineGuid", out var g1) && g1.ValueKind == JsonValueKind.String)
            return ParseGuidLoose(g1.GetString());

        if (line.TryGetProperty("WorkOrderLineGUID", out var g2) && g2.ValueKind == JsonValueKind.String)
            return ParseGuidLoose(g2.GetString());

        if (line.TryGetProperty("WorkOrderLineId", out var g3) && g3.ValueKind == JsonValueKind.String)
            return ParseGuidLoose(g3.GetString());

        return null;
    }

    private static void WriteMissingCanonicalDates(
        bool hasRpcWorkingDate,
        bool hasTransactionDate,
        bool hasOperationDate,
        string? opsLiteral,
        Utf8JsonWriter w)
    {
        if (!string.IsNullOrWhiteSpace(opsLiteral))
        {
            if (!hasRpcWorkingDate) w.WriteString("RPCWorkingDate", opsLiteral);
            if (!hasTransactionDate) w.WriteString("TransactionDate", opsLiteral);
            if (!hasOperationDate) w.WriteString("OperationDate", opsLiteral);
        }
    }

    private static void WritePreferExistingElseEnrichedDate(JsonElement current, string? opsLiteral, Utf8JsonWriter w)
    {
        if (!IsNullOrBlank(current))
        {
            current.WriteTo(w);
            return;
        }

        if (!string.IsNullOrWhiteSpace(opsLiteral))
        {
            w.WriteStringValue(opsLiteral);
            return;
        }

        current.WriteTo(w);
    }

    private static bool IsNullOrBlank(JsonElement v)
        => v.ValueKind == JsonValueKind.Null
        || v.ValueKind == JsonValueKind.Undefined
        || (v.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(v.GetString()));

    private static string? TryGetString(JsonElement obj, string propertyName)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return null;

        if (!obj.TryGetProperty(propertyName, out var prop))
            return null;

        return prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static string? NormalizeToFscmDateLiteralOrNull(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var s = raw.Trim();

        if (s.StartsWith("/Date(", StringComparison.Ordinal) && s.EndsWith(")/", StringComparison.Ordinal))
            return s;

        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
            return $"/Date({dto.ToUnixTimeMilliseconds()})/";

        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return $"/Date({new DateTimeOffset(dt).ToUnixTimeMilliseconds()})/";

        return MissingDateSentinel;
    }
}
