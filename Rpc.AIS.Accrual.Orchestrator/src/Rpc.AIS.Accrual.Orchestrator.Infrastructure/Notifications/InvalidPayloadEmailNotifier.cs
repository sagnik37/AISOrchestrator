using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Notifications;

/// <summary>
/// Logs AIS-side validation failures in structured form instead of sending emails.
/// This is invoked BEFORE any posting attempt so that invalid records are never sent to FSCM.
/// </summary>
public sealed class InvalidPayloadEmailNotifier : IInvalidPayloadNotifier
{
    private readonly ILogger<InvalidPayloadEmailNotifier> _logger;

    public InvalidPayloadEmailNotifier(
        ILogger<InvalidPayloadEmailNotifier> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task NotifyAsync(
        RunContext context,
        JournalType journalType,
        IReadOnlyList<WoPayloadValidationFailure> failures,
        int workOrdersBefore,
        int workOrdersAfter,
        CancellationToken ct)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (failures is null || failures.Count == 0)
        {
            _logger.LogInformation(
                "AIS_VALIDATION_SUMMARY RunId={RunId} CorrelationId={CorrelationId} JournalType={JournalType} WorkOrdersBefore={WorkOrdersBefore} WorkOrdersAfter={WorkOrdersAfter} FailureCount={FailureCount}",
                context.RunId,
                context.CorrelationId,
                journalType,
                workOrdersBefore,
                workOrdersAfter,
                0);

            return Task.CompletedTask;
        }

        _logger.LogWarning(
            "AIS_VALIDATION_SUMMARY RunId={RunId} CorrelationId={CorrelationId} JournalType={JournalType} WorkOrdersBefore={WorkOrdersBefore} WorkOrdersAfter={WorkOrdersAfter} FailureCount={FailureCount}",
            context.RunId,
            context.CorrelationId,
            journalType,
            workOrdersBefore,
            workOrdersAfter,
            failures.Count);

        foreach (var f in failures)
        {
            _logger.LogError(
                "AIS_VALIDATION_DETAIL RunId={RunId} CorrelationId={CorrelationId} JournalType={JournalType} WorkOrderGuid={WorkOrderGuid} WorkOrderId={WorkOrderId} WorkOrderLineGuid={WorkOrderLineGuid} Code={Code} Message={Message}",
                context.RunId,
                context.CorrelationId,
                journalType,
                FormatGuid(f.WorkOrderGuid),
                NullIfWhiteSpace(f.WorkOrderNumber),
                FormatGuid(f.WorkOrderLineGuid),
                NullIfWhiteSpace(f.Code),
                NullIfWhiteSpace(f.Message));
        }

        return Task.CompletedTask;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? FormatGuid(Guid value) =>
        value == Guid.Empty ? null : $"{{{value}}}";

    private static string? FormatGuid(Guid? value) =>
        value.HasValue && value.Value != Guid.Empty
            ? $"{{{value.Value}}}"
            : null;
}
