using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services;

/// <summary>
/// Orchestrates FSCM custom validation for normalized WO payload JSON.
/// Behavior:
/// - Never drops the whole WO due to invalid lines (line filtering is handled by local rules and/or FSCM filteredPayloadJson)
/// - Fail-closed on FSCM validation transport failure (returns 0 WorkOrdersAfter)
/// - Returns counts derived from input vs filtered payload
/// </summary>
public sealed class WoPayloadValidationEngine : IWoPayloadValidationEngine
{
    private static readonly IReadOnlyList<WoPayloadValidationFailure> NoFailures =
        Array.Empty<WoPayloadValidationFailure>();

    private readonly IFscmWoPayloadValidationClient _fscmClient;
    private readonly ILogger<WoPayloadValidationEngine> _logger;

    public WoPayloadValidationEngine(
        IFscmWoPayloadValidationClient fscmClient,
        ILogger<WoPayloadValidationEngine> logger)
    {
        _fscmClient = fscmClient ?? throw new ArgumentNullException(nameof(fscmClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<WoPayloadValidationResult> ValidateAndFilterAsync(
        RunContext ctx,
        JournalType journalType,
        string normalizedWoPayloadJson,
        CancellationToken ct)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));

        var before = CountWorkOrders(normalizedWoPayloadJson);

        if (string.IsNullOrWhiteSpace(normalizedWoPayloadJson) || before == 0)
        {
            return new WoPayloadValidationResult(
                filteredPayloadJson: "{}",
                failures: NoFailures,
                workOrdersBefore: before,
                workOrdersAfter: 0);
        }

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["RunId"] = ctx.RunId,
            ["CorrelationId"] = ctx.CorrelationId,
            ["JournalType"] = journalType.ToString(),
            ["WorkOrdersBefore"] = before
        });

        var remote = await _fscmClient
            .ValidateAsync(ctx, journalType, normalizedWoPayloadJson, ct)
            .ConfigureAwait(false);

        // FAIL CLOSED: if HTTP transport failed, do not proceed further.
        if (!remote.IsSuccessStatusCode)
        {
            _logger.LogError(
                "FSCM custom validation call failed. StatusCode={StatusCode} Url={Url}",
                remote.StatusCode, remote.Url);

            // Return "after=0" so pipeline can halt deterministically upstream.
            // Do not invent failures here (keep Core independent + avoid guessing constructor requirements for failures).
            return new WoPayloadValidationResult(
                filteredPayloadJson: normalizedWoPayloadJson, // preserve input for troubleshooting
                failures: NoFailures,
                workOrdersBefore: before,
                workOrdersAfter: 0);
        }

        // If FSCM returns header-level Invalid failures (no WorkOrderLineGuid), we cannot safely
        // filter individual lines. Treat this as a blocking validation failure and stop posting
        // to prevent duplicate journal creation attempts.
        var hasBlockingHeaderInvalid = remote.Failures is { Count: > 0 } && remote.Failures.Any(f =>
            f.Disposition == ValidationDisposition.Invalid &&
            (f.WorkOrderLineGuid is null || f.WorkOrderLineGuid == Guid.Empty));
        if (hasBlockingHeaderInvalid)
        {
            _logger.LogWarning(
                "FSCM custom validation returned blocking Invalid failures (header-level). WorkOrdersBefore={Before} WorkOrdersAfter=0 FailureCount={FailureCount}",
                before, remote.Failures!.Count);
            return new WoPayloadValidationResult(
                filteredPayloadJson: "{}",
                failures: remote.Failures ?? NoFailures,
                workOrdersBefore: before,
                workOrdersAfter: 0);
        }

        // Log any FSCM-reported failures (but don't drop entire WO here).
        if (remote.Failures is { Count: > 0 })
        {
            foreach (var f in remote.Failures)
            {
                _logger.LogWarning(
                    "FSCM validation failure. Code={Code} Message={Message} Disposition={Disposition}",
                    f.Code, f.Message, f.Disposition);
            }
        }

        var filteredJson = string.IsNullOrWhiteSpace(remote.FilteredPayloadJson) ? "{}" : remote.FilteredPayloadJson;
        var after = CountWorkOrders(filteredJson);

        return new WoPayloadValidationResult(
            filteredPayloadJson: filteredJson,
            failures: remote.Failures ?? NoFailures,
            workOrdersBefore: before,
            workOrdersAfter: after);
    }

    private static int CountWorkOrders(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return 0;

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            if (!TryGetProperty(root, "_request", out var req))
                return 0;

            if (!TryGetProperty(req, "WOList", out var woList))
                return 0;

            return woList.ValueKind == JsonValueKind.Array
                ? woList.GetArrayLength()
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var p in root.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
