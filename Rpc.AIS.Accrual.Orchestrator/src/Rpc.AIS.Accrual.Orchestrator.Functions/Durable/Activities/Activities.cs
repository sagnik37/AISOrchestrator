using Microsoft.Azure.Functions.Worker;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Functions.Services;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Durable activity adapter only. All business logic lives in <see cref="ActivitiesUseCase"/>.
/// </summary>
public sealed class Activities
{
    private readonly IActivitiesUseCase _useCase;

    public Activities(IActivitiesUseCase useCase)
        => _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));

    [Function(nameof(ValidateAndPostWoPayload))]
    public Task<List<PostResult>> ValidateAndPostWoPayload(
        [ActivityTrigger] DurableAccrualOrchestration.WoPayloadPostingInputDto input,
        FunctionContext ctx)
        => _useCase.ValidateAndPostWoPayloadAsync(input, CreateRunContext(input.RunId, input.CorrelationId, input.TriggeredBy, ExtractDataAreaIdFromWoPayload(input.WoPayloadJson)), ctx.CancellationToken);

    [Function(nameof(PostSingleWorkOrder))]
    public Task<PostSingleWorkOrderResponse> PostSingleWorkOrder(
        [ActivityTrigger] DurableAccrualOrchestration.SingleWoPostingInputDto input,
        FunctionContext ctx)
        => _useCase.PostSingleWorkOrderAsync(input, CreateRunContext(input.RunId, input.CorrelationId, input.TriggeredBy), ctx.CancellationToken);

    [Function(nameof(UpdateWorkOrderStatus))]
    public Task<WorkOrderStatusUpdateResponse> UpdateWorkOrderStatus(
        [ActivityTrigger] DurableAccrualOrchestration.WorkOrderStatusUpdateInputDto input,
        FunctionContext ctx)
        => _useCase.UpdateWorkOrderStatusAsync(input, CreateRunContext(input.RunId, input.CorrelationId, input.TriggeredBy), ctx.CancellationToken);

    [Function(nameof(PostRetryableWoPayload))]
    public Task<PostResult> PostRetryableWoPayload(
        [ActivityTrigger] DurableAccrualOrchestration.RetryableWoPayloadPostingInputDto input,
        FunctionContext ctx)
        => _useCase.PostRetryableWoPayloadAsync(input, CreateRunContext(input.RunId, input.CorrelationId, input.TriggeredBy, ExtractDataAreaIdFromWoPayload(input.WoPayloadJson)), ctx.CancellationToken);

    [Function(nameof(SyncInvoiceAttributes))]
    public Task<DurableAccrualOrchestration.InvoiceAttributesSyncResultDto> SyncInvoiceAttributes(
        [ActivityTrigger] DurableAccrualOrchestration.InvoiceAttributesSyncInputDto input,
        FunctionContext ctx)
        => _useCase.SyncInvoiceAttributesAsync(input, CreateRunContext(input.RunId, input.CorrelationId, input.TriggeredBy, ExtractDataAreaIdFromWoPayload(input.WoPayloadJson)), ctx.CancellationToken);

    [Function(nameof(FinalizeAndNotifyWoPayload))]
    public Task<DurableAccrualOrchestration.RunOutcomeDto> FinalizeAndNotifyWoPayload(
        [ActivityTrigger] DurableAccrualOrchestration.FinalizeWoPayloadInputDto input,
        FunctionContext ctx)
        => _useCase.FinalizeAndNotifyWoPayloadAsync(input, CreateRunContext(input.RunId, input.CorrelationId, input.TriggeredBy, ExtractDataAreaIdFromWoPayload(input.WoPayloadJson)), ctx.CancellationToken);

    private static RunContext CreateRunContext(string runId, string correlationId, string? triggeredBy, string? dataAreaId = null)
        => new(runId, DateTimeOffset.UtcNow, triggeredBy, correlationId, null, dataAreaId);

    private static string? ExtractDataAreaIdFromWoPayload(string? payloadJson)
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
            // Best-effort extraction only. Do not break existing activity flow on malformed payloads.
        }

        return null;
    }
}
