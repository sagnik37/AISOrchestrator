using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.Enrichment;

internal sealed class FsExtrasInjector : IFsExtrasInjector
{
    private readonly ILogger _log;

    public FsExtrasInjector(ILogger log)
        => _log = log ?? throw new ArgumentNullException(nameof(log));

    public string InjectFsExtrasAndLogPerWoSummary(
        string payloadJson,
        IReadOnlyDictionary<Guid, FsLineExtras> extrasByLineGuid,
        string runId,
        string corr)
    {
        using var input = JsonDocument.Parse(payloadJson);

        var stats = new List<WoEnrichmentStats>();

        // Downstream injector currently expects a concrete Dictionary.
        // Preserve behavior while allowing callers to pass any IReadOnlyDictionary.
        var extrasDict = extrasByLineGuid as Dictionary<Guid, FsLineExtras>
            ?? new Dictionary<Guid, FsLineExtras>(extrasByLineGuid);

        using var ms = new MemoryStream();
        using var w = new Utf8JsonWriter(ms);

        FsaDeltaPayloadJsonInjector.CopyRootWithInjectionAndStats(
            input.RootElement,
            w,
            extrasDict,
            stats);

        w.Flush();
        var updated = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        foreach (var s in stats)
        {
            _log.LogInformation(
                "WO Enrichment Summary " +
                "WorkorderGUID={WorkorderGuid} WorkorderID={WorkorderId} Company={Company} " +
                "EnrichedLinesTotal={Total} Hour={Hour} Expense={Expense} Item={Item} " +
                "Currency={Currency} ResourceId={ResourceId} Warehouse={Warehouse} Site={Site} LineNum={LineNum} OperationsDate={OperationsDate}",
                s.WorkorderGuidRaw,
                s.WorkorderId,
                s.Company,
                s.EnrichedLinesTotal,
                s.EnrichedHourLines,
                s.EnrichedExpLines,
                s.EnrichedItemLines,
                s.FilledCurrency,
                s.FilledResourceId,
                s.FilledWarehouse,
                s.FilledSite,
                s.FilledLineNum,
                s.FilledOperationsDate); // NEW
        }

        return updated;
    }
}
