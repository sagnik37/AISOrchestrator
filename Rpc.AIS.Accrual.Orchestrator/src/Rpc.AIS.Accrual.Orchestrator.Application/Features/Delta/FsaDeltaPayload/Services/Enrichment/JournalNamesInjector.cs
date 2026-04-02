using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.Enrichment;

internal sealed class JournalNamesInjector : IJournalNamesInjector
{
    private readonly ILogger _log;

    public JournalNamesInjector(ILogger log)
        => _log = log ?? throw new ArgumentNullException(nameof(log));

    public string InjectJournalNamesIntoPayload(
        string payloadJson,
        IReadOnlyDictionary<string, LegalEntityJournalNames> journalNamesByCompany)
    {
        if (journalNamesByCompany is null || journalNamesByCompany.Count == 0)
            return payloadJson;

        using var input = JsonDocument.Parse(payloadJson);

        using var ms = new MemoryStream();
        using var w = new Utf8JsonWriter(ms);

        FsaDeltaPayloadEnricher.CopyRootWithJournalNamesInjection(input.RootElement, w, journalNamesByCompany);

        w.Flush();
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}
