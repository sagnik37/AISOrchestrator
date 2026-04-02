using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.Enrichment;

internal sealed class JournalDescriptionsStamper : IJournalDescriptionsStamper
{
    private readonly ILogger _log;

    public JournalDescriptionsStamper(ILogger log)
        => _log = log ?? throw new ArgumentNullException(nameof(log));

    public string StampJournalDescriptionsIntoPayload(string payloadJson, string action)
    {
        if (string.IsNullOrWhiteSpace(action))
            action = "Post";

        using var input = JsonDocument.Parse(payloadJson);

        using var ms = new MemoryStream();
        using var w = new Utf8JsonWriter(ms);

        FsaDeltaPayloadEnricher.CopyRootWithJournalDescriptionStamp(input.RootElement, w, action.Trim());

        w.Flush();
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}
