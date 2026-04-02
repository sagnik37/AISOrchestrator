using Microsoft.Extensions.Logging;

using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Functions.Functions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using System.Diagnostics;
using System.Text;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services.Handlers;

public sealed class PostSingleWorkOrderHandler : ActivitiesHandlerBase
{
    private readonly ISingleWorkOrderPostingClient _singleWo;
    private readonly IAisLogger _ais;
    private readonly ILogger<PostSingleWorkOrderHandler> _logger;

    public PostSingleWorkOrderHandler(
        ISingleWorkOrderPostingClient singleWo,
        IAisLogger ais,
        ILogger<PostSingleWorkOrderHandler> logger)
    {
        _singleWo = singleWo ?? throw new ArgumentNullException(nameof(singleWo));
        _ais = ais ?? throw new ArgumentNullException(nameof(ais));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PostSingleWorkOrderResponse> HandleAsync(
        DurableAccrualOrchestration.SingleWoPostingInputDto input,
        RunContext runCtx,
        CancellationToken ct)
    {
        using var scope = BeginScope(_logger, runCtx, "PostSingleWorkOrder", input.DurableInstanceId);

        var body = input.RawJsonBody ?? string.Empty;

        _logger.LogInformation(
            "Activity PostSingleWorkOrder. RunId={RunId} CorrelationId={CorrelationId} PayloadBytes={Bytes}",
            runCtx.RunId, runCtx.CorrelationId, Encoding.UTF8.GetByteCount(body));

        try
        {
            var sw = Stopwatch.StartNew();

            _logger.LogInformation(
                "Activity PostSingleWorkOrder: Calling FSCM single WO posting. RunId={RunId} CorrelationId={CorrelationId}",
                runCtx.RunId, runCtx.CorrelationId);

            var resp = await _singleWo.PostAsync(runCtx, body, ct)
                       ?? new PostSingleWorkOrderResponse(false, 500, "{\"error\":\"Null response from FSCM posting client.\"}");

            sw.Stop();

            _logger.LogInformation(
                "Activity PostSingleWorkOrder: FSCM posting returned. ElapsedMs={ElapsedMs} StatusCode={StatusCode} Success={Success} RunId={RunId} CorrelationId={CorrelationId}",
                sw.ElapsedMilliseconds, resp.StatusCode, resp.IsSuccess, runCtx.RunId, runCtx.CorrelationId);

            await _ais.InfoAsync(runCtx.RunId, "FSCM", "Single work order posting completed.", new
            {
                runCtx.CorrelationId,
                resp.StatusCode,
                resp.IsSuccess
            }, ct);

            return resp;
        }
        catch (Exception ex)
        {
            await _ais.ErrorAsync(runCtx.RunId, "FSCM", "Single work order posting failed (exception).", ex, new
            {
                runCtx.CorrelationId
            }, ct);

            throw; // allow Durable retry policy to apply
        }
    }
}
