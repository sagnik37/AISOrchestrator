using Microsoft.Extensions.Logging;

using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Functions.Functions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using System.Text;
using System.Diagnostics;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services.Handlers;

public sealed class UpdateWorkOrderStatusHandler : ActivitiesHandlerBase
{
    private readonly IWorkOrderStatusUpdateClient _woStatus;
    private readonly IAisLogger _ais;
    private readonly ILogger<UpdateWorkOrderStatusHandler> _logger;

    public UpdateWorkOrderStatusHandler(
        IWorkOrderStatusUpdateClient woStatus,
        IAisLogger ais,
        ILogger<UpdateWorkOrderStatusHandler> logger)
    {
        _woStatus = woStatus ?? throw new ArgumentNullException(nameof(woStatus));
        _ais = ais ?? throw new ArgumentNullException(nameof(ais));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<WorkOrderStatusUpdateResponse> HandleAsync(
        DurableAccrualOrchestration.WorkOrderStatusUpdateInputDto input,
        RunContext runCtx,
        CancellationToken ct)
    {
        using var scope = BeginScope(_logger, runCtx, "UpdateWorkOrderStatus", input.DurableInstanceId);

        var body = input.RawJsonBody ?? string.Empty;

        _logger.LogInformation(
            "Activity UpdateWorkOrderStatus. RunId={RunId} CorrelationId={CorrelationId} PayloadBytes={Bytes}",
            runCtx.RunId, runCtx.CorrelationId, Encoding.UTF8.GetByteCount(body));

        try
        {
            var sw = Stopwatch.StartNew();

            _logger.LogInformation(
                "Activity UpdateWorkOrderStatus: Calling FSCM status update. RunId={RunId} CorrelationId={CorrelationId}",
                runCtx.RunId, runCtx.CorrelationId);

            var resp = await _woStatus.UpdateAsync(runCtx, body, ct)
                       ?? new WorkOrderStatusUpdateResponse(false, 500, "{\"error\":\"Null response from FSCM status update client.\"}");

            sw.Stop();

            _logger.LogInformation(
                "Activity UpdateWorkOrderStatus: FSCM status update returned. ElapsedMs={ElapsedMs} StatusCode={StatusCode} Success={Success} RunId={RunId} CorrelationId={CorrelationId}",
                sw.ElapsedMilliseconds, resp.StatusCode, resp.IsSuccess, runCtx.RunId, runCtx.CorrelationId);

            await _ais.InfoAsync(runCtx.RunId, "FSCM", "Work order status update completed.", new
            {
                runCtx.CorrelationId,
                resp.StatusCode,
                resp.IsSuccess
            }, ct);

            return resp;
        }
        catch (Exception ex)
        {
            await _ais.ErrorAsync(runCtx.RunId, "FSCM", "Work order status update failed (exception).", ex, new
            {
                runCtx.CorrelationId
            }, ct);

            throw; // allow Durable retry policy to apply
        }
    }
}
