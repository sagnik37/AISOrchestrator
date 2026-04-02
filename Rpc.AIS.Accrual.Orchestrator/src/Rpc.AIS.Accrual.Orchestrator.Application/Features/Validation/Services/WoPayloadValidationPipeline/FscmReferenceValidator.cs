using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;
using Rpc.AIS.Accrual.Orchestrator.Core.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.WoPayloadValidationPipeline;

/// <summary>
/// Optional FSCM-backed reference validation (custom endpoint) that can mark lines/WO as retryable/invalid.
/// </summary>
public sealed class FscmReferenceValidator : IFscmReferenceValidator
{
    private readonly ILogger<FscmReferenceValidator> _logger;
    private readonly PayloadValidationOptions _options;
    private readonly IFscmCustomValidationClient? _fscmCustomValidator;

    public FscmReferenceValidator(
        ILogger<FscmReferenceValidator> logger,
        IOptions<PayloadValidationOptions> options,
        IFscmCustomValidationClient? fscmCustomValidator)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new PayloadValidationOptions();
        _fscmCustomValidator = fscmCustomValidator;
    }

    public async Task ApplyFscmCustomValidationAsync(
        RunContext context,
        JournalType journalType,
        List<WoPayloadValidationFailure> invalidFailures,
        List<WoPayloadValidationFailure> retryableFailures,
        List<FilteredWorkOrder> validWorkOrders,
        List<FilteredWorkOrder> retryableWorkOrders,
        Stopwatch stopwatch,
        CancellationToken ct)
    {
        if (!ShouldRunFscmCustomValidation(validWorkOrders))
            return;

        var groups = GroupValidWorkOrdersByCompany(validWorkOrders);

        var remoteFailures = await ExecuteRemoteCompanyValidationsAsync(
                context,
                journalType,
                groups,
                ct)
            .ConfigureAwait(false);

        if (remoteFailures.Count == 0)
            return;

        ClassifyRemoteFailures(remoteFailures, invalidFailures, retryableFailures);

        if (ContainsFailFast(remoteFailures))
        {
            HandleFailFast(context, journalType, remoteFailures.Count, stopwatch, validWorkOrders, retryableWorkOrders);
            return;
        }

        ApplyRemoteWorkOrderFiltering(remoteFailures, validWorkOrders, retryableWorkOrders);
    }

    private bool ShouldRunFscmCustomValidation(List<FilteredWorkOrder> validWorkOrders)
        => _options.EnableFscmCustomEndpointValidation && _fscmCustomValidator is not null && validWorkOrders.Count > 0;

    private static IEnumerable<IGrouping<string, (string Company, FilteredWorkOrder WorkOrder)>> GroupValidWorkOrdersByCompany(
    List<FilteredWorkOrder> validWorkOrders)
        => validWorkOrders
            .Select(wo =>
            {
                var company = WoPayloadJsonHelpers.TryGetString(wo.WorkOrder, "Company") ?? string.Empty;
                return (Company: company, WorkOrder: wo);
            })
            .GroupBy(x => x.Company, StringComparer.OrdinalIgnoreCase);

    private async Task<List<WoPayloadValidationFailure>> ExecuteRemoteCompanyValidationsAsync(
        RunContext context,
        JournalType journalType,
        IEnumerable<IGrouping<string, (string Company, FilteredWorkOrder WorkOrder)>> groups,
        CancellationToken ct)
    {
        var remoteFailures = new List<WoPayloadValidationFailure>();

        foreach (var g in groups)
        {
            if (ct.IsCancellationRequested) break;

            var company = g.Key ?? string.Empty;
            var payloadForCompany = WoPayloadJsonBuilder.BuildFilteredPayloadJson(g.Select(x => x.WorkOrder).ToList());

            var failures = await _fscmCustomValidator!
                .ValidateAsync(context, journalType, company, payloadForCompany, ct)
                .ConfigureAwait(false);

            if (failures is { Count: > 0 })
                remoteFailures.AddRange(failures);
        }

        return remoteFailures;
    }

    private static bool ContainsFailFast(IReadOnlyCollection<WoPayloadValidationFailure> failures)
        => failures.Any(f => f.Disposition == ValidationDisposition.FailFast);

    private void HandleFailFast(
        RunContext context,
        JournalType journalType,
        int failureCount,
        Stopwatch sw,
        List<FilteredWorkOrder> validWorkOrders,
        List<FilteredWorkOrder> retryableWorkOrders)
    {
        sw.Stop();
        _logger.LogWarning(
            "AIS validation fail-fast due to FSCM custom validation endpoint. RunId={RunId} CorrelationId={CorrelationId} JournalType={JournalType} FailureCount={Count} ElapsedMs={ElapsedMs}",
            context.RunId, context.CorrelationId, journalType, failureCount, sw.ElapsedMilliseconds);

        // Fail-fast returns empty postable payloads, but preserves failure lists.
        validWorkOrders.Clear();
        retryableWorkOrders.Clear();
    }

    private static void ClassifyRemoteFailures(
        IReadOnlyList<WoPayloadValidationFailure> remoteFailures,
        List<WoPayloadValidationFailure> invalidFailures,
        List<WoPayloadValidationFailure> retryableFailures)
    {
        foreach (var f in remoteFailures)
        {
            switch (f.Disposition)
            {
                case ValidationDisposition.Retryable:
                    retryableFailures.Add(f);
                    break;
                case ValidationDisposition.FailFast:
                case ValidationDisposition.Invalid:
                case ValidationDisposition.Valid:
                default:
                    invalidFailures.Add(f);
                    break;
            }
        }
    }

    private static void ApplyRemoteWorkOrderFiltering(
        IReadOnlyList<WoPayloadValidationFailure> remoteFailures,
        List<FilteredWorkOrder> validWorkOrders,
        List<FilteredWorkOrder> retryableWorkOrders)
    {
        // Conservatively classify at WO level: any remote failure on a WO excludes it from postable payload.
        var byWo = remoteFailures
            .Where(f => f.WorkOrderGuid != Guid.Empty)
            .GroupBy(f => f.WorkOrderGuid)
            .ToDictionary(
                g2 => g2.Key,
                g2 =>
                {
                    var hasInvalid = g2.Any(x => x.Disposition == ValidationDisposition.Invalid);
                    var hasRetry = g2.Any(x => x.Disposition == ValidationDisposition.Retryable);
                    return hasRetry && !hasInvalid ? ValidationDisposition.Retryable : ValidationDisposition.Invalid;
                });

        if (byWo.Count == 0)
            return;

        var movedToRetry = new List<FilteredWorkOrder>();

        validWorkOrders.RemoveAll(wo =>
        {
            var id =
                WoPayloadJsonHelpers.TryGetGuid(wo.WorkOrder, "WorkOrderGUID")
                ?? WoPayloadJsonHelpers.TryGetGuid(wo.WorkOrder, "RPCWorkOrderGuid")
                ?? Guid.Empty;

            if (id == Guid.Empty) return false;

            if (!byWo.TryGetValue(id, out var disp))
                return false;

            if (disp == ValidationDisposition.Retryable)
                movedToRetry.Add(wo);

            return true; // remove from valid list
        });

        foreach (var wo in movedToRetry)
        {
            var id =
                WoPayloadJsonHelpers.TryGetGuid(wo.WorkOrder, "WorkOrderGUID")
                ?? WoPayloadJsonHelpers.TryGetGuid(wo.WorkOrder, "RPCWorkOrderGuid")
                ?? Guid.Empty;

            if (id == Guid.Empty) continue;

            if (!retryableWorkOrders.Any(r =>
                (WoPayloadJsonHelpers.TryGetGuid(r.WorkOrder, "WorkOrderGUID") ?? WoPayloadJsonHelpers.TryGetGuid(r.WorkOrder, "RPCWorkOrderGuid") ?? Guid.Empty) == id))
            {
                retryableWorkOrders.Add(wo);
            }
        }
    }
}
