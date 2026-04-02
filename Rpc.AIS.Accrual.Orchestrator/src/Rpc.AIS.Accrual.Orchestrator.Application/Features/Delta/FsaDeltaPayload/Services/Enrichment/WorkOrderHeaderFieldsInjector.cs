using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.Enrichment;

internal sealed class WorkOrderHeaderFieldsInjector : IWorkOrderHeaderFieldsInjector
{
    private readonly ILogger _log;

    public WorkOrderHeaderFieldsInjector(ILogger log)
        => _log = log ?? throw new ArgumentNullException(nameof(log));

    public string InjectWorkOrderHeaderFieldsIntoPayload(
        string payloadJson,
        IReadOnlyDictionary<Guid, WoHeaderMappingFields> woIdToHeaderFields)
    {
        if (woIdToHeaderFields is null || woIdToHeaderFields.Count == 0)
            return payloadJson;

        using var input = JsonDocument.Parse(payloadJson);

        using var ms = new MemoryStream();
        using var w = new Utf8JsonWriter(ms);

        FsaDeltaPayloadEnricher.CopyRootWithWoHeaderFieldsInjection(input.RootElement, w, woIdToHeaderFields);

        w.Flush();
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}
