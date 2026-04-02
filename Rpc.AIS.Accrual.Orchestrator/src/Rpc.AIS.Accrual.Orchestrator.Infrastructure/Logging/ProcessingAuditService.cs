using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Processing;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

public sealed class ProcessingAuditService : IProcessingAuditService
{
    private readonly ILogger<ProcessingAuditService> _logger;

    public ProcessingAuditService(ILogger<ProcessingAuditService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public RunAuditSummary CreateSummary(RunContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        return new RunAuditSummary
        {
            RunId = context.RunId ?? string.Empty,
            CorrelationId = context.CorrelationId,
            TriggeredBy = context.TriggeredBy
        };
    }

    public void AddSuccess(
        RunAuditSummary summary,
        string workOrderNumber,
        string stage,
        string? workOrderGuid = null,
        string? journalType = null)
    {
        if (summary is null) throw new ArgumentNullException(nameof(summary));

        summary.Records.Add(new WorkOrderAuditRecord
        {
            WorkOrderNumber = workOrderNumber ?? string.Empty,
            WorkOrderGuid = workOrderGuid,
            Success = true,
            Stage = stage ?? string.Empty,
            Reason = null,
            JournalType = journalType
        });
    }

    public void AddFailure(
        RunAuditSummary summary,
        string workOrderNumber,
        string stage,
        string reason,
        string? workOrderGuid = null,
        string? journalType = null)
    {
        if (summary is null) throw new ArgumentNullException(nameof(summary));

        summary.Records.Add(new WorkOrderAuditRecord
        {
            WorkOrderNumber = workOrderNumber ?? string.Empty,
            WorkOrderGuid = workOrderGuid,
            Success = false,
            Stage = stage ?? string.Empty,
            Reason = reason,
            JournalType = journalType
        });
    }

    public void LogSummary(RunAuditSummary summary)
    {
        if (summary is null) throw new ArgumentNullException(nameof(summary));

        _logger.LogInformation(
            "AIS_RUN_SUMMARY RunId={RunId} CorrelationId={CorrelationId} TriggeredBy={TriggeredBy} Total={Total} SuccessCount={SuccessCount} FailureCount={FailureCount}",
            summary.RunId,
            summary.CorrelationId,
            summary.TriggeredBy,
            summary.Total,
            summary.SuccessCount,
            summary.FailureCount);

        foreach (var record in summary.Records.OrderBy(x => x.WorkOrderNumber, StringComparer.OrdinalIgnoreCase))
        {
            if (record.Success)
            {
                _logger.LogInformation(
                    "AIS_WORKORDER_RESULT RunId={RunId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid} Status={Status} Stage={Stage} JournalType={JournalType} Reason={Reason}",
                    summary.RunId,
                    record.WorkOrderNumber,
                    record.WorkOrderGuid,
                    "Success",
                    record.Stage,
                    record.JournalType,
                    (string?)null);
            }
            else
            {
                _logger.LogWarning(
                    "AIS_WORKORDER_RESULT RunId={RunId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid} Status={Status} Stage={Stage} JournalType={JournalType} Reason={Reason}",
                    summary.RunId,
                    record.WorkOrderNumber,
                    record.WorkOrderGuid,
                    "Failed",
                    record.Stage,
                    record.JournalType,
                    record.Reason);
            }
        }
    }
}
