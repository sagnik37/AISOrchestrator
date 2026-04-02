using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services;

/// <summary>
/// Provides status change service behavior.
/// </summary>
public sealed class StatusChangeService : IStatusChangeService
{
    private readonly IAisLogger _ais;

    public StatusChangeService(IAisLogger ais)
    {
        _ais = ais ?? throw new ArgumentNullException(nameof(ais));
    }

    /// <summary>
    /// Executes handle async.
    /// </summary>
    public Task HandleAsync(StatusChangeRequest request, CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var runId = string.IsNullOrWhiteSpace(request.RunId) ? "RUN-NA" : request.RunId!;
        var step = "StatusChange";

        var data = new
        {
            request.EntityName,
            request.RecordId,
            request.OldStatus,
            request.NewStatus,
            request.CorrelationId,
            request.Message,
            request.Payload
        };

        return _ais.InfoAsync(runId, step, "Status change received.", data, ct);
    }
}
