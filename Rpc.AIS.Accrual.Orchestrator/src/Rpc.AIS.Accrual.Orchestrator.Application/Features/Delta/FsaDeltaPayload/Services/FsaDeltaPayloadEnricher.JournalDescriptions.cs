// File: .../FsaDeltaPayloadEnricher.JournalDescriptions.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.Enrichment;
using static Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.FsaDeltaPayloadJsonUtil;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload;

public sealed partial class FsaDeltaPayloadEnricher
{
    public string StampJournalDescriptionsIntoPayload(string payloadJson, string action)
        {
            return _journalDescriptions.StampJournalDescriptionsIntoPayload(payloadJson, action);
        }

    internal static void CopyRootWithJournalDescriptionStamp(JsonElement root, Utf8JsonWriter w, string action)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                root.WriteTo(w);
                return;
            }

            w.WriteStartObject();

            foreach (var p in root.EnumerateObject())
            {
                if (p.NameEquals("_request") && p.Value.ValueKind == JsonValueKind.Object)
                {
                    w.WritePropertyName(p.Name);
                    CopyRequestWithJournalDescriptionStamp(p.Value, w, action);
                }
                else
                {
                    w.WritePropertyName(p.Name);
                    p.Value.WriteTo(w);
                }
            }

            w.WriteEndObject();
        }

    private static void CopyRequestWithJournalDescriptionStamp(JsonElement req, Utf8JsonWriter w, string action)
        {
            w.WriteStartObject();

            foreach (var p in req.EnumerateObject())
            {
                if (p.NameEquals("WOList") && p.Value.ValueKind == JsonValueKind.Array)
                {
                    w.WritePropertyName("WOList");
                    w.WriteStartArray();

                    foreach (var wo in p.Value.EnumerateArray())
                        CopyWoWithJournalDescriptionStamp(wo, w, action);

                    w.WriteEndArray();
                }
                else
                {
                    w.WritePropertyName(p.Name);
                    p.Value.WriteTo(w);
                }
            }

            w.WriteEndObject();
        }

    private static void CopyWoWithJournalDescriptionStamp(JsonElement wo, Utf8JsonWriter w, string action)
        {
            // Read final header values (after any previous enrichment).
            string jobId = string.Empty;
            string subProjectId = string.Empty;

            if (wo.ValueKind == JsonValueKind.Object)
            {
                if (wo.TryGetProperty("WorkOrderID", out var j) && j.ValueKind == JsonValueKind.String)
                    jobId = j.GetString() ?? string.Empty;

                if (wo.TryGetProperty("SubProjectId", out var sp) && sp.ValueKind == JsonValueKind.String)
                    subProjectId = sp.GetString() ?? string.Empty;
            }

            var desc = $"{jobId} - {subProjectId} - {action}";

            w.WriteStartObject();

            foreach (var p in wo.EnumerateObject())
            {
                if ((p.NameEquals("WOItemLines") || p.NameEquals("WOExpLines") || p.NameEquals("WOHourLines")) &&
                    p.Value.ValueKind == JsonValueKind.Object)
                {
                    w.WritePropertyName(p.Name);
                    CopyJournalWithDescriptionStamp(p.Value, w, desc);
                    continue;
                }

                w.WritePropertyName(p.Name);
                p.Value.WriteTo(w);
            }

            w.WriteEndObject();
        }

    private static void CopyJournalWithDescriptionStamp(JsonElement journal, Utf8JsonWriter w, string desc)
        {
            w.WriteStartObject();

            foreach (var p in journal.EnumerateObject())
            {
                if (p.NameEquals("JournalDescription"))
                {
                    w.WritePropertyName("JournalDescription");
                    w.WriteStringValue(desc);
                    continue;
                }

                if (p.NameEquals("JournalLines") && p.Value.ValueKind == JsonValueKind.Array)
                {
                    w.WritePropertyName("JournalLines");
                    w.WriteStartArray();

                    foreach (var ln in p.Value.EnumerateArray())
                        CopyLineWithDescriptionStamp(ln, w, desc);

                    w.WriteEndArray();
                    continue;
                }

                w.WritePropertyName(p.Name);
                p.Value.WriteTo(w);
            }

            // Ensure JournalDescription exists even if it was missing.
            if (!journal.TryGetProperty("JournalDescription", out _))
            {
                w.WritePropertyName("JournalDescription");
                w.WriteStringValue(desc);
            }

            w.WriteEndObject();
        }

    private static void CopyLineWithDescriptionStamp(JsonElement line, Utf8JsonWriter w, string desc)
    {
        if (line.ValueKind != JsonValueKind.Object)
        {
            line.WriteTo(w);
            return;
        }

        w.WriteStartObject();

        var wroteJld = false;

        foreach (var p in line.EnumerateObject())
        {
            if (p.NameEquals("JournalLineDescription"))
            {
                wroteJld = true;

                // Preserve existing value EXACTLY as provided by FS payload.
                // Do NOT stamp JournalDescription when blank.
                w.WritePropertyName("JournalLineDescription");

                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    var existing = p.Value.GetString();
                    w.WriteStringValue(existing ?? string.Empty);
                }
                else if (p.Value.ValueKind == JsonValueKind.Null ||
                         p.Value.ValueKind == JsonValueKind.Undefined)
                {
                    w.WriteStringValue(string.Empty);
                }
                else
                {
                    p.Value.WriteTo(w);
                }

                continue;
            }

            w.WritePropertyName(p.Name);
            p.Value.WriteTo(w);
        }

        // If JournalLineDescription did not exist in the payload,
        // add it but leave it blank (do NOT stamp JournalDescription).
        if (!wroteJld)
        {
            w.WritePropertyName("JournalLineDescription");
            w.WriteStringValue(string.Empty);
        }

        w.WriteEndObject();
    }
}
