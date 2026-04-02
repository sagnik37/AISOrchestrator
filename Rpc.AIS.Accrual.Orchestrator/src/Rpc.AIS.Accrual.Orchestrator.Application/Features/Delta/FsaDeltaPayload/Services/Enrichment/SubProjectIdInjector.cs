using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.Enrichment;

internal sealed class SubProjectIdInjector : ISubProjectIdInjector
{
    private readonly ILogger _log;

    public SubProjectIdInjector(ILogger log)
        => _log = log ?? throw new ArgumentNullException(nameof(log));

    public string InjectSubProjectIdIntoPayload(
        string payloadJson,
        IReadOnlyDictionary<Guid, string> woIdToSubProjectId)
    {
        if (woIdToSubProjectId is null || woIdToSubProjectId.Count == 0)
            return payloadJson;

        using var input = JsonDocument.Parse(payloadJson);

        using var ms = new MemoryStream();
        using var w = new Utf8JsonWriter(ms);

        FsaDeltaPayloadEnricher.CopyRootWithSubProjectIdInjection(input.RootElement, w, woIdToSubProjectId);

        w.Flush();
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}
