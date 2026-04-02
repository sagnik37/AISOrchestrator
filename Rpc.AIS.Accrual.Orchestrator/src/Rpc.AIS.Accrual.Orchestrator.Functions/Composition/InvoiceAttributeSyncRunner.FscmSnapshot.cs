using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.InvoiceAttributes;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.InvoiceAttributes;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services;

/// <summary>
/// Builds invoice attribute delta (FS -> FSCM) using Work Order header fields and FSCM snapshots,
/// and injects the computed InvoiceAttributes into the posting payload.
/// 
/// :
/// - This component MUST NOT call FSCM "update" endpoints directly.
/// - Posting pipeline (FscmJournalPoster) is the single place that performs the actual update,
///   and it must happen AFTER successful journal posting (hard dependent).
///
/// Rules:
/// - FS is system-of-record: FS overrides FSCM.
/// - If FS value is null but FSCM has value, AIS clears FSCM (sets null).
/// - Outbound payload uses FSCM attribute names (via Fs->Fscm mapping) OR fixed FSCM names for derived comparisons.
///
/// Mapping source of truth (for standard fields):
/// - FSCM AttributeTypeGlobalAttributes (Name, FSA)
///
/// Option B behavior:
/// - For each (Company, SubProjectId), FSCM definitions + current values are fetched ONCE and cached in-memory for the run.
/// - Comparisons/delta are performed against the in-memory snapshot.
/// </summary>
public sealed partial class InvoiceAttributeSyncRunner
{
    private async Task<HashSet<string>> GetActiveDefinitionsAsync(
        RunContext ctx,
        SubProjectKey key,
        Dictionary<SubProjectKey, HashSet<string>> defsCache,
        CancellationToken ct)
    {
        if (defsCache.TryGetValue(key, out var cached))
            return cached;

        var defs = await _fscmReadOnly.GetDefinitionsAsync(ctx, key.Company, key.SubProjectId, ct).ConfigureAwait(false);

        var active = new HashSet<string>(
            defs.Where(d => d is not null && d.Active && !string.IsNullOrWhiteSpace(d.AttributeName))
                .Select(d => d.AttributeName!),
            StringComparer.OrdinalIgnoreCase);

        defsCache[key] = active;
        return active;
    }

    private async Task<Dictionary<string, string?>> GetCurrentSnapshotAsync(
        RunContext ctx,
        SubProjectKey key,
        IEnumerable<string> requiredNames,
        Dictionary<SubProjectKey, Dictionary<string, string?>> currentCache,
        CancellationToken ct)
    {
        if (currentCache.TryGetValue(key, out var cached))
            return cached;

        var names = requiredNames?.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>();
        var current = await _fscmReadOnly.GetCurrentValuesAsync(ctx, key.Company, key.SubProjectId, names, ct).ConfigureAwait(false);

        var dict = current
            .Where(p => !string.IsNullOrWhiteSpace(p.AttributeName))
            .GroupBy(p => p.AttributeName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g2 => g2.Key, g2 => g2.Last().AttributeValue, StringComparer.OrdinalIgnoreCase);

        currentCache[key] = dict;
        return dict;
    }

    private static void ApplyUpdatesToCurrentSnapshot(
        SubProjectKey key,
        IReadOnlyList<InvoiceAttributePair> updates,
        Dictionary<SubProjectKey, Dictionary<string, string?>> currentCache)
    {
        if (!currentCache.TryGetValue(key, out var dict) || dict is null)
            return;

        foreach (var u in updates)
        {
            if (string.IsNullOrWhiteSpace(u.AttributeName)) continue;
            dict[u.AttributeName] = u.AttributeValue;
        }
    }

    private static string InjectInvoiceAttributesIntoPostingPayload(
        string postingPayloadJson,
        IReadOnlyDictionary<Guid, IReadOnlyList<InvoiceAttributePair>> updatesByWoGuid)
    {
        using var doc = JsonDocument.Parse(postingPayloadJson);

        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false });

        var root = doc.RootElement;

        writer.WriteStartObject();

        foreach (var prop in root.EnumerateObject())
        {
            if (!prop.NameEquals("_request"))
            {
                prop.WriteTo(writer);
                continue;
            }

            // _request
            writer.WritePropertyName("_request");
            writer.WriteStartObject();

            if (prop.Value.ValueKind != JsonValueKind.Object)
            {
                writer.WriteEndObject();
                continue;
            }

            foreach (var reqProp in prop.Value.EnumerateObject())
            {
                if (!reqProp.NameEquals("WOList"))
                {
                    reqProp.WriteTo(writer);
                    continue;
                }

                // WOList
                writer.WritePropertyName("WOList");
                writer.WriteStartArray();

                if (reqProp.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var wo in reqProp.Value.EnumerateArray())
                    {
                        if (wo.ValueKind != JsonValueKind.Object)
                        {
                            wo.WriteTo(writer);
                            continue;
                        }

                        if (!TryReadWorkOrderGuid(wo, out var woGuid) || !updatesByWoGuid.TryGetValue(woGuid, out var updates) || updates.Count == 0)
                        {
                            // No enrichment for this WO; copy as-is.
                            wo.WriteTo(writer);
                            continue;
                        }

                        writer.WriteStartObject();

                        // Copy all existing properties EXCEPT any existing InvoiceAttributes (we replace).
                        foreach (var woProp in wo.EnumerateObject())
                        {
                            if (woProp.NameEquals("InvoiceAttributes"))
                                continue;

                            woProp.WriteTo(writer);
                        }

                        // Inject InvoiceAttributes array
                        writer.WritePropertyName("InvoiceAttributes");
                        writer.WriteStartArray();
                        foreach (var u in updates)
                        {
                            if (string.IsNullOrWhiteSpace(u.AttributeName)) continue;

                            writer.WriteStartObject();
                            writer.WriteString("AttributeName", u.AttributeName);
                            if (u.AttributeValue is null)
                                writer.WriteNull("AttributeValue");
                            else
                                writer.WriteString("AttributeValue", u.AttributeValue);
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();

                        writer.WriteEndObject();
                    }
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject(); // _request object
        }

        writer.WriteEndObject(); // root
        writer.Flush();

        return System.Text.Encoding.UTF8.GetString(ms.ToArray());

        static bool TryReadWorkOrderGuid(JsonElement wo, out Guid woGuid)
        {
            woGuid = Guid.Empty;
            if (!wo.TryGetProperty("WorkOrderGUID", out var p)) return false;

            var s = p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString();
            if (string.IsNullOrWhiteSpace(s)) return false;

            s = s.Trim();
            if (s.StartsWith("{") && s.EndsWith("}"))
                s = s.Trim('{', '}');

            return Guid.TryParse(s, out woGuid);
        }
    }
}
