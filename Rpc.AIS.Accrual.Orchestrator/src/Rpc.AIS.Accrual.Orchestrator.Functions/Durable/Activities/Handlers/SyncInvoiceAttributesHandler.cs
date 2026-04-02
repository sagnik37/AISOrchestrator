using Microsoft.Extensions.Logging;

using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Functions.Functions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services.Handlers;

public sealed class SyncInvoiceAttributesHandler : ActivitiesHandlerBase
{
    private readonly InvoiceAttributeSyncRunner _invoiceSync;
    private readonly InvoiceAttributesUpdateRunner _invoiceUpdate;
    private readonly ILogger<SyncInvoiceAttributesHandler> _logger;

    public SyncInvoiceAttributesHandler(
        InvoiceAttributeSyncRunner invoiceSync,
        InvoiceAttributesUpdateRunner invoiceUpdate,
        ILogger<SyncInvoiceAttributesHandler> logger)
    {
        _invoiceSync = invoiceSync ?? throw new ArgumentNullException(nameof(invoiceSync));
        _invoiceUpdate = invoiceUpdate ?? throw new ArgumentNullException(nameof(invoiceUpdate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DurableAccrualOrchestration.InvoiceAttributesSyncResultDto> HandleAsync(
        DurableAccrualOrchestration.InvoiceAttributesSyncInputDto input,
        RunContext runCtx,
        CancellationToken ct)
    {
        using var scope = BeginScope(_logger, runCtx, "SyncInvoiceAttributes", input.DurableInstanceId);

        var json = input.WoPayloadJson ?? string.Empty;
        if (string.IsNullOrWhiteSpace(json))
            return new DurableAccrualOrchestration.InvoiceAttributesSyncResultDto(false, true, 0, 0, "Empty payload; skipped.", 0, 0);

        try
        {
            var enrich = await _invoiceSync.EnrichPostingPayloadAsync(runCtx, json, ct).ConfigureAwait(false);
            var upd = await _invoiceUpdate.UpdateFromPostingPayloadAsync(runCtx, enrich.PostingPayloadJson, ct).ConfigureAwait(false);

            return new DurableAccrualOrchestration.InvoiceAttributesSyncResultDto(
                enrich.Attempted,
                enrich.Success,
                enrich.WorkOrdersWithInvoiceAttributes,
                enrich.TotalAttributePairs,
                enrich.Note,
                upd.SuccessCount,
                upd.FailureCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Activity SyncInvoiceAttributes FAILED. RunId={RunId} CorrelationId={CorrelationId}", runCtx.RunId, runCtx.CorrelationId);
            return new DurableAccrualOrchestration.InvoiceAttributesSyncResultDto(true, false, 0, 0, ex.Message, 0, 1);
        }
    }
}
