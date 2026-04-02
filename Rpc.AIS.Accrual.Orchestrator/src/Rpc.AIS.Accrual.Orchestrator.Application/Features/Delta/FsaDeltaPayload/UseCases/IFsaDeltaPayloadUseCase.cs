using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Core.UseCases.FsaDeltaPayload;

public interface IFsaDeltaPayloadUseCase
{
    Task<GetFsaDeltaPayloadResultDto> BuildFullFetchAsync(GetFsaDeltaPayloadInputDto input, FsaDeltaPayloadRunOptions opt, CancellationToken ct);

    /// <summary>
    /// Builds a WO payload for a single work order GUID even if it is not in the OPEN set.
    /// Intended for job-operation flows (e.g., Cancel) where the WO may already be closed/cancelled in FSA.
    /// </summary>
    Task<GetFsaDeltaPayloadResultDto> BuildSingleWorkOrderAnyStatusAsync(GetFsaDeltaPayloadInputDto input, FsaDeltaPayloadRunOptions opt, CancellationToken ct);
}
