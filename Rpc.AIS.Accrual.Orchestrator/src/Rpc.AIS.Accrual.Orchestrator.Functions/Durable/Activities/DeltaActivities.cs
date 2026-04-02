using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Utils;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Durable activity that converts an FS (Dataverse) payload into a delta-only payload
/// by comparing against FSCM journal history.
///
/// </summary>
public sealed class DeltaActivities
{
    private readonly IWoDeltaPayloadService _delta;
    private readonly IAisLogger _ais;
    private readonly ILogger<DeltaActivities> _log;
    private readonly IOptions<FscmOptions> _fscm;

    public DeltaActivities(
        IWoDeltaPayloadService delta,
        IAisLogger ais,
        IOptions<FscmOptions> fscm,
        ILogger<DeltaActivities> log)
    {
        _delta = delta ?? throw new ArgumentNullException(nameof(delta));
        _ais = ais ?? throw new ArgumentNullException(nameof(ais));
        _fscm = fscm ?? throw new ArgumentNullException(nameof(fscm));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    [Function(nameof(BuildDeltaPayloadFromFscmHistory))]
    public async Task<BuildDeltaPayloadFromFscmHistoryResultDto> BuildDeltaPayloadFromFscmHistory(
        [ActivityTrigger] BuildDeltaPayloadFromFscmHistoryInputDto input,
        FunctionContext ctx)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));

        var company = ExtractDataAreaIdFromFsaPayload(input.FsaPayloadJson);
        var runCtx = new RunContext(
                                    input.RunId,
                                    DateTimeOffset.UtcNow,
                                    input.TriggeredBy,
                                    input.CorrelationId,
                                    DataAreaId: company);

        using var scope = LogScopes.BeginFunctionScope(_log, new LogScopeContext
        {
            Activity = nameof(BuildDeltaPayloadFromFscmHistory),
            Operation = nameof(BuildDeltaPayloadFromFscmHistory),
            Trigger = input.TriggeredBy,
            RunId = input.RunId,
            CorrelationId = input.CorrelationId,
            SourceSystem = "AIS",
            DurableInstanceId = input.DurableInstanceId
        });


        // Keep Trigger as additional dimension (optional but recommended)
        using var triggerScope = _log.BeginScope(new Dictionary<string, object?>
        {
            ["Trigger"] = input.TriggeredBy
        });



        var fsaJson = input.FsaPayloadJson ?? string.Empty;
        var fsaBytes = Encoding.UTF8.GetByteCount(fsaJson);
        var fsaSha = Sha256Hex(fsaJson);

        _log.LogInformation(
            "DeltaActivity START. FsaBytes={Bytes} FsaSha256={Sha256}",
            fsaBytes,
            fsaSha);

        await _ais.InfoAsync(input.RunId, "Delta", "Delta activity started.", new
        {
            input.CorrelationId,
            FsaBytes = fsaBytes,
            FsaSha256 = fsaSha,
            TodayUtc = JobRunDateResolver.GetRunDate(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        }, ctx.CancellationToken).ConfigureAwait(false);

        var sw = Stopwatch.StartNew();

        try
        {
            // todayUtc used by  period resolver / reversal planning logic
            var result = await _delta.BuildDeltaPayloadAsync(
                    runCtx,
                    fsaJson,
                    JobRunDateResolver.GetRunDate(DateTime.UtcNow),
                    ctx.CancellationToken)
                .ConfigureAwait(false);

            sw.Stop();

            var deltaJson = result.DeltaPayloadJson ?? string.Empty;
            var deltaBytes = Encoding.UTF8.GetByteCount(deltaJson);
            var deltaSha = Sha256Hex(deltaJson);

            //  property names in WoDeltaPayloadBuildResult are TotalDeltaLines/TotalReverseLines/TotalRecreateLines
            _log.LogInformation(
                "DeltaActivity END. ElapsedMs={ElapsedMs} WorkOrdersIn={In} WorkOrdersOut={Out} TotalDeltaLines={DeltaLines} TotalReverseLines={Reverse} TotalRecreateLines={Recreate} DeltaBytes={Bytes} DeltaSha256={Sha256}",
                sw.ElapsedMilliseconds,
                result.WorkOrdersInInput,
                result.WorkOrdersInOutput,
                result.TotalDeltaLines,
                result.TotalReverseLines,
                result.TotalRecreateLines,
                deltaBytes,
                deltaSha);

            await _ais.InfoAsync(input.RunId, "Delta", "Delta payload built.", new
            {
                input.CorrelationId,
                sw.ElapsedMilliseconds,
                result.WorkOrdersInInput,
                result.WorkOrdersInOutput,
                TotalDeltaLines = result.TotalDeltaLines,
                TotalReverseLines = result.TotalReverseLines,
                TotalRecreateLines = result.TotalRecreateLines,
                DeltaBytes = deltaBytes,
                DeltaSha256 = deltaSha
            }, ctx.CancellationToken).ConfigureAwait(false);

            // Map to  activity DTO (keeps  orchestrator contract stable)
            return new BuildDeltaPayloadFromFscmHistoryResultDto(
                DeltaPayloadJson: deltaJson,
                WorkOrdersInInput: result.WorkOrdersInInput,
                WorkOrdersInOutput: result.WorkOrdersInOutput,
                DeltaLines: result.TotalDeltaLines,
                ReverseLines: result.TotalReverseLines,
                RecreateLines: result.TotalRecreateLines);
        }
        catch (Exception ex)
        {
            sw.Stop();

            _log.LogError(ex, "DeltaActivity FAILED. ElapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);

            await _ais.ErrorAsync(input.RunId, "Delta", "Delta activity failed.", ex, new
            {
                input.CorrelationId,
                sw.ElapsedMilliseconds,
                FsaBytes = fsaBytes,
                FsaSha256 = fsaSha
            }, ctx.CancellationToken).ConfigureAwait(false);

            throw;
        }
    }

    /// <summary>
    /// Executes sha 256 hex.
    /// </summary>
    private static string? ExtractDataAreaIdFromFsaPayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (!doc.RootElement.TryGetProperty("_request", out var request) || request.ValueKind != JsonValueKind.Object)
                return null;
            if (!request.TryGetProperty("WOList", out var woList) || woList.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var wo in woList.EnumerateArray())
            {
                if (wo.ValueKind != JsonValueKind.Object) continue;
                foreach (var name in new[] { "Company", "LegalEntity", "DataAreaId", "dataAreaId" })
                {
                    if (wo.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                    {
                        var s = v.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(s)) return s;
                    }
                }
            }
        }
        catch
        {
            // Best-effort extraction only. Delta build already has payload; do not fail if enrichment cannot read it.
        }

        return null;
    }

    private static string Sha256Hex(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s ?? string.Empty);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
