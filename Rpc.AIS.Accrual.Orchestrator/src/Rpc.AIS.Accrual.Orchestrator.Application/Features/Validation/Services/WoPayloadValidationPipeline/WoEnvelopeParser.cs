using System;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.WoPayloadValidationPipeline;

public sealed class WoEnvelopeParser : IWoEnvelopeParser
{
    private readonly ILogger<WoEnvelopeParser> _logger;

    public WoEnvelopeParser(ILogger<WoEnvelopeParser> logger)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public bool TryGetWoList(
        RunContext context,
        JournalType journalType,
        JsonElement root,
        out JsonElement woList,
        out WoPayloadValidationResult? failureResult)
    {
        woList = default;
        failureResult = null;

        // Envelope can be:
        //  - { "_request": { "WOList": [...] } }
        //  - { "request": { "WOList": [...] } }
        //  - { "WOList": [...] }  (already request-shaped)
        JsonElement request = default;
        bool hasRequest =
            (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("_request", out request) && request.ValueKind == JsonValueKind.Object) ||
            (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("request", out request) && request.ValueKind == JsonValueKind.Object) ||
            (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("WOList", out _) && (request = root).ValueKind == JsonValueKind.Object);

        if (!hasRequest)
        {
            var fail = new WoPayloadValidationFailure(
                WorkOrderGuid: Guid.Empty,
                WorkOrderNumber: null,
                JournalType: journalType,
                WorkOrderLineGuid: null,
                Code: "AIS_PAYLOAD_MISSING_REQUEST",
                Message: "Payload missing _request (or request) object / WOList.",
                Disposition: ValidationDisposition.Invalid);

            _logger.LogWarning(
                "AIS validation failed at envelope level. RunId={RunId} CorrelationId={CorrelationId} JournalType={JournalType} Code={Code}",
                context.RunId, context.CorrelationId, journalType, fail.Code);

            failureResult = new WoPayloadValidationResult(
                "{}",
                new[] { fail },
                0,
                0,
                "{}",
                Array.Empty<WoPayloadValidationFailure>(),
                0);

            return false;
        }

        if (!request.TryGetProperty("WOList", out woList) || woList.ValueKind != JsonValueKind.Array)
        {
            var fail = new WoPayloadValidationFailure(
                Guid.Empty,
                null,
                journalType,
                null,
                "AIS_PAYLOAD_MISSING_WOLIST",
                "Payload missing WOList array.",
                ValidationDisposition.Invalid);

            _logger.LogWarning(
                "AIS validation failed: missing WOList. RunId={RunId} CorrelationId={CorrelationId} JournalType={JournalType}",
                context.RunId, context.CorrelationId, journalType);

            failureResult = new WoPayloadValidationResult(
                "{}",
                new[] { fail },
                0,
                0,
                "{}",
                Array.Empty<WoPayloadValidationFailure>(),
                0);

            return false;
        }

        return true;
    }
}
