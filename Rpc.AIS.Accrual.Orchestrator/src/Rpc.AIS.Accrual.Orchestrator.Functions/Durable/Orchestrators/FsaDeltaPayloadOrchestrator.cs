// File: .../Functions/Services/FsaDeltaPayloadOrchestrator.cs
// - Functions layer is now a thin adapter.
// - All delta payload orchestration lives in Core.UseCases.FsaDeltaPayload.
//

using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.UseCases.FsaDeltaPayload;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services;

public interface IFsaDeltaPayloadOrchestrator
{
    Task<GetFsaDeltaPayloadResultDto> BuildFullFetchAsync(GetFsaDeltaPayloadInputDto input, FsOptions opt, CancellationToken ct);

    Task<GetFsaDeltaPayloadResultDto> BuildSingleWorkOrderAnyStatusAsync(GetFsaDeltaPayloadInputDto input, FsOptions opt, CancellationToken ct);
}

public sealed class FsaDeltaPayloadOrchestrator : IFsaDeltaPayloadOrchestrator
{
    private readonly IFsaDeltaPayloadUseCase _useCase;

    public FsaDeltaPayloadOrchestrator(IFsaDeltaPayloadUseCase useCase)
        => _useCase = useCase ?? throw new System.ArgumentNullException(nameof(useCase));

    public Task<GetFsaDeltaPayloadResultDto> BuildFullFetchAsync(GetFsaDeltaPayloadInputDto input, FsOptions opt, CancellationToken ct)
    {
        var runOpt = new FsaDeltaPayloadRunOptions
        {
            WorkOrderFilter = opt?.WorkOrderFilter
        };

        return _useCase.BuildFullFetchAsync(input, runOpt, ct);
    }

    public Task<GetFsaDeltaPayloadResultDto> BuildSingleWorkOrderAnyStatusAsync(GetFsaDeltaPayloadInputDto input, FsOptions opt, CancellationToken ct)
    {
        // WorkOrderFilter is not required for single-any-status.
        var runOpt = new FsaDeltaPayloadRunOptions
        {
            WorkOrderFilter = opt?.WorkOrderFilter
        };

        return _useCase.BuildSingleWorkOrderAnyStatusAsync(input, runOpt, ct);
    }
}
