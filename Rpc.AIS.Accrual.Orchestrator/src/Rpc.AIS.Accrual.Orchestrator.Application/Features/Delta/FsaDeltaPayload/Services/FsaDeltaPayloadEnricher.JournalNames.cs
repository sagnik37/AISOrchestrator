// File: .../FsaDeltaPayloadEnricher.JournalNames.cs

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
    public string InjectJournalNamesIntoPayload(
            string payloadJson,
            IReadOnlyDictionary<string, LegalEntityJournalNames> journalNamesByCompany)
        {
            return _journalNames.InjectJournalNamesIntoPayload(payloadJson, journalNamesByCompany);
        }

    internal static void CopyRootWithJournalNamesInjection(
            JsonElement root,
            Utf8JsonWriter w,
            IReadOnlyDictionary<string, LegalEntityJournalNames> journalNamesByCompany)
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
                    CopyRequestWithJournalNamesInjection(p.Value, w, journalNamesByCompany);
                }
                else
                {
                    w.WritePropertyName(p.Name);
                    p.Value.WriteTo(w);
                }
            }

            w.WriteEndObject();
        }

    private static void CopyRequestWithJournalNamesInjection(
            JsonElement req,
            Utf8JsonWriter w,
            IReadOnlyDictionary<string, LegalEntityJournalNames> journalNamesByCompany)
        {
            w.WriteStartObject();

            foreach (var p in req.EnumerateObject())
            {
                if (p.NameEquals("WOList") && p.Value.ValueKind == JsonValueKind.Array)
                {
                    w.WritePropertyName("WOList");
                    w.WriteStartArray();

                    foreach (var wo in p.Value.EnumerateArray())
                        CopyWoWithJournalNamesInjection(wo, w, journalNamesByCompany);

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

    private static void CopyWoWithJournalNamesInjection(
            JsonElement wo,
            Utf8JsonWriter w,
            IReadOnlyDictionary<string, LegalEntityJournalNames> journalNamesByCompany)
        {
            string? company = null;
            if (wo.ValueKind == JsonValueKind.Object && wo.TryGetProperty("Company", out var c) && c.ValueKind == JsonValueKind.String)
                company = c.GetString();

            LegalEntityJournalNames? names = null;
            if (!string.IsNullOrWhiteSpace(company) && journalNamesByCompany.TryGetValue(company!.Trim(), out var v))
                names = v;

            w.WriteStartObject();

            foreach (var p in wo.EnumerateObject())
            {
                if ((p.NameEquals("WOItemLines") || p.NameEquals("WOExpLines") || p.NameEquals("WOHourLines")) &&
                    p.Value.ValueKind == JsonValueKind.Object)
                {
                    w.WritePropertyName(p.Name);
                    CopyJournalHeaderWithName(p.Name, p.Value, w, names);
                    continue;
                }

                w.WritePropertyName(p.Name);
                p.Value.WriteTo(w);
            }

            w.WriteEndObject();
        }
}
