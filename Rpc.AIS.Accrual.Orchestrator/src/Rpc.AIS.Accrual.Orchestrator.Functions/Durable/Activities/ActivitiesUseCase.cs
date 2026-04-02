using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;
using Rpc.AIS.Accrual.Orchestrator.Functions.Functions;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Functions.Services.Handlers;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services;

/// <summary>
/// Implements activity business logic (Activities.cs becomes an orchestration adapter only).
/// </summary>
public sealed partial class ActivitiesUseCase : IActivitiesUseCase
{
    private readonly ILogger<ActivitiesUseCase> _logger;

    private readonly ValidateAndPostWoPayloadHandler _validateAndPost;
    private readonly PostSingleWorkOrderHandler _postSingle;
    private readonly UpdateWorkOrderStatusHandler _updateStatus;
    private readonly PostRetryableWoPayloadHandler _postRetryable;
    private readonly SyncInvoiceAttributesHandler _syncInvoice;
    private readonly FinalizeAndNotifyWoPayloadHandler _finalizeAndNotify;

    public ActivitiesUseCase(
        ValidateAndPostWoPayloadHandler validateAndPost,
        PostSingleWorkOrderHandler postSingle,
        UpdateWorkOrderStatusHandler updateStatus,
        PostRetryableWoPayloadHandler postRetryable,
        SyncInvoiceAttributesHandler syncInvoice,
        FinalizeAndNotifyWoPayloadHandler finalizeAndNotify,
        ILogger<ActivitiesUseCase> logger)
    {
        _validateAndPost = validateAndPost ?? throw new ArgumentNullException(nameof(validateAndPost));
        _postSingle = postSingle ?? throw new ArgumentNullException(nameof(postSingle));
        _updateStatus = updateStatus ?? throw new ArgumentNullException(nameof(updateStatus));
        _postRetryable = postRetryable ?? throw new ArgumentNullException(nameof(postRetryable));
        _syncInvoice = syncInvoice ?? throw new ArgumentNullException(nameof(syncInvoice));
        _finalizeAndNotify = finalizeAndNotify ?? throw new ArgumentNullException(nameof(finalizeAndNotify));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<PostResult>> ValidateAndPostWoPayloadAsync(
        DurableAccrualOrchestration.WoPayloadPostingInputDto input,
        RunContext runCtx,
        CancellationToken ct)
    {
        return await _validateAndPost.HandleAsync(input, runCtx, ct);
    }

    public async Task<PostSingleWorkOrderResponse> PostSingleWorkOrderAsync(
        DurableAccrualOrchestration.SingleWoPostingInputDto input,
        RunContext runCtx,
        CancellationToken ct)
    {
        return await _postSingle.HandleAsync(input, runCtx, ct);
    }

    public async Task<WorkOrderStatusUpdateResponse> UpdateWorkOrderStatusAsync(
        DurableAccrualOrchestration.WorkOrderStatusUpdateInputDto input,
        RunContext runCtx,
        CancellationToken ct)
    {
        return await _updateStatus.HandleAsync(input, runCtx, ct);
    }

    public async Task<PostResult> PostRetryableWoPayloadAsync(
        DurableAccrualOrchestration.RetryableWoPayloadPostingInputDto input,
        RunContext runCtx,
        CancellationToken ct)
    {
        return await _postRetryable.HandleAsync(input, runCtx, ct);
    }

    public async Task<DurableAccrualOrchestration.InvoiceAttributesSyncResultDto> SyncInvoiceAttributesAsync(
        DurableAccrualOrchestration.InvoiceAttributesSyncInputDto input,
        RunContext runCtx,
        CancellationToken ct)
    {
        return await _syncInvoice.HandleAsync(input, runCtx, ct);
    }

    public async Task<DurableAccrualOrchestration.RunOutcomeDto> FinalizeAndNotifyWoPayloadAsync(
        DurableAccrualOrchestration.FinalizeWoPayloadInputDto input,
        RunContext runCtx,
        CancellationToken ct)
    {
        return await _finalizeAndNotify.HandleAsync(input, runCtx, ct);
    }
}
