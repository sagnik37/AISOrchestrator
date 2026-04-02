// File: .../Core/UseCases/FsaDeltaPayload/*
//
// - Moves delta payload orchestration into Core (UseCase layer) and splits the orchestrator into partials.
// - Functions layer becomes a thin adapter.


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

public sealed partial class FsaDeltaPayloadEnricher : IFsaDeltaPayloadEnricher
{
    private readonly ILogger<FsaDeltaPayloadEnricher> _log;
    private readonly IFsExtrasInjector _fsExtras;
    private readonly ISubProjectIdInjector _subProjectId;
    private readonly IWorkOrderHeaderFieldsInjector _woHeader;
    private readonly IJournalNamesInjector _journalNames;
    private readonly IJournalDescriptionsStamper _journalDescriptions;
    private readonly ICompanyInjector _company;

    public FsaDeltaPayloadEnricher(ILogger<FsaDeltaPayloadEnricher> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));

        // Composition: keep injectors cohesive and independently testable.
        _fsExtras = new FsExtrasInjector(_log);
        _subProjectId = new SubProjectIdInjector(_log);
        _woHeader = new WorkOrderHeaderFieldsInjector(_log);
        _journalNames = new JournalNamesInjector(_log);
        _journalDescriptions = new JournalDescriptionsStamper(_log);
        _company = new CompanyInjector(_log);
    }
    public string InjectFsExtrasAndLogPerWoSummary(
            string payloadJson,
            Dictionary<Guid, FsLineExtras> extrasByLineGuid,
            string runId,
            string corr)
    {
        return _fsExtras.InjectFsExtrasAndLogPerWoSummary(payloadJson, extrasByLineGuid, runId, corr);
    }

    private static void CopyJournalHeaderWithName(
        string sectionKey,
        JsonElement journal,
        Utf8JsonWriter w,
        LegalEntityJournalNames? names)
    {
        // Determine journal name per section.
        var journalName = sectionKey switch
        {
            "WOItemLines" => names?.InventJournalNameId,
            "WOExpLines" => names?.ExpenseJournalNameId,
            "WOHourLines" => names?.HourJournalNameId,
            _ => null
        };

        w.WriteStartObject();

        var wroteJournalName = false;

        foreach (var p in journal.EnumerateObject())
        {
            if (p.NameEquals("JournalName"))
            {
                wroteJournalName = true;
                w.WritePropertyName("JournalName");
                w.WriteStringValue(journalName ?? (p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? string.Empty : string.Empty));
                continue;
            }

            w.WritePropertyName(p.Name);
            p.Value.WriteTo(w);
        }

        if (!wroteJournalName)
        {
            w.WritePropertyName("JournalName");
            w.WriteStringValue(journalName ?? string.Empty);
        }

        w.WriteEndObject();
    }


    
    // <summary>
    // Executes build work order number map.
    // </summary>
}
