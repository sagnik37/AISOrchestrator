using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;
using Rpc.AIS.Accrual.Orchestrator.Core.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.WoPayloadValidationPipeline;

public sealed class WoValidationResultBuilder : IWoValidationResultBuilder
{
    private readonly ILogger<WoValidationResultBuilder> _logger;
    private readonly PayloadValidationOptions _options;

    public WoValidationResultBuilder(
        ILogger<WoValidationResultBuilder> logger,
        IOptions<PayloadValidationOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new PayloadValidationOptions();
    }

    public WoPayloadValidationResult BuildResult(
        RunContext context,
        JournalType journalType,
        int workOrdersBefore,
        List<FilteredWorkOrder> validWorkOrders,
        List<FilteredWorkOrder> retryableWorkOrders,
        List<WoPayloadValidationFailure> invalidFailures,
        List<WoPayloadValidationFailure> retryableFailures,
        Stopwatch stopwatch)
    {
        var validAfter = validWorkOrders.Count;
        var retryAfter = retryableWorkOrders.Count;

        var filteredJson = WoPayloadJsonBuilder.BuildFilteredPayloadJson(validWorkOrders);
        var retryJson = WoPayloadJsonBuilder.BuildFilteredPayloadJson(retryableWorkOrders);

        stopwatch.Stop();

        LogSummary(
            context,
            journalType,
            workOrdersBefore,
            validAfter,
            retryAfter,
            invalidFailures.Count,
            retryableFailures.Count,
            stopwatch.ElapsedMilliseconds);

        return new WoPayloadValidationResult(
            filteredJson,
            invalidFailures,
            workOrdersBefore,
            validAfter,
            retryJson,
            retryableFailures,
            retryAfter);
    }

    private void LogSummary(
        RunContext context,
        JournalType journalType,
        int beforeCount,
        int validAfter,
        int retryAfter,
        int invalidCount,
        int retryableCount,
        long elapsedMs)
    {
        _logger.LogInformation(
            "AIS validation complete. RunId={RunId} CorrelationId={CorrelationId} JournalType={JournalType} WorkOrdersBefore={Before} ValidAfter={ValidAfter} RetryableAfter={RetryAfter} InvalidFailureCount={InvalidCount} RetryableFailureCount={RetryableCount} ElapsedMs={ElapsedMs} FscmCustom={FscmCustom} DropWholeWO={DropWhole}",
            context.RunId,
            context.CorrelationId,
            journalType,
            beforeCount,
            validAfter,
            retryAfter,
            invalidCount,
            retryableCount,
            elapsedMs,
            _options.EnableFscmCustomEndpointValidation,
            _options.DropWholeWorkOrderOnAnyInvalidLine);
    }
}
