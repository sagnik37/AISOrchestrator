using System.Threading;
using System.Collections.Generic;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services;

public interface IActivitiesUseCase
{
    Task<List<PostResult>> ValidateAndPostWoPayloadAsync(
        DurableAccrualOrchestration.WoPayloadPostingInputDto input,
        RunContext runCtx,
        CancellationToken ct);

    Task<PostSingleWorkOrderResponse> PostSingleWorkOrderAsync(
        DurableAccrualOrchestration.SingleWoPostingInputDto input,
        RunContext runCtx,
        CancellationToken ct);

    Task<WorkOrderStatusUpdateResponse> UpdateWorkOrderStatusAsync(
        DurableAccrualOrchestration.WorkOrderStatusUpdateInputDto input,
        RunContext runCtx,
        CancellationToken ct);

    Task<PostResult> PostRetryableWoPayloadAsync(
        DurableAccrualOrchestration.RetryableWoPayloadPostingInputDto input,
        RunContext runCtx,
        CancellationToken ct);

    Task<DurableAccrualOrchestration.InvoiceAttributesSyncResultDto> SyncInvoiceAttributesAsync(
        DurableAccrualOrchestration.InvoiceAttributesSyncInputDto input,
        RunContext runCtx,
        CancellationToken ct);

    Task<DurableAccrualOrchestration.RunOutcomeDto> FinalizeAndNotifyWoPayloadAsync(
        DurableAccrualOrchestration.FinalizeWoPayloadInputDto input,
        RunContext runCtx,
        CancellationToken ct);
}
