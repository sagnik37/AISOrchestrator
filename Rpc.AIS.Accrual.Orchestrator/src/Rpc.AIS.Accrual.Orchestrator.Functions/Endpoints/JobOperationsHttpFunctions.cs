using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Backward-compatible facade for the previous shared job operations handler.
/// The SOLID refactor moves endpoint logic into endpoint-specific use cases.
/// </summary>
public sealed partial class JobOperationsHttpHandlerCommon : IJobOperationsHttpUseCase
{
    private readonly IAdHocSingleJobUseCase _adHocSingle;
    private readonly IPostJobUseCase _postJob;
    private readonly ICancelJobUseCase _cancelJob;
    private readonly ICustomerChangeUseCase _customerChange;

    public JobOperationsHttpHandlerCommon(
        IAdHocSingleJobUseCase adHocSingle,
        IPostJobUseCase postJob,
        ICancelJobUseCase cancelJob,
        ICustomerChangeUseCase customerChange)
    {
        _adHocSingle = adHocSingle ?? throw new ArgumentNullException(nameof(adHocSingle));
        _postJob = postJob ?? throw new ArgumentNullException(nameof(postJob));
        _cancelJob = cancelJob ?? throw new ArgumentNullException(nameof(cancelJob));
        _customerChange = customerChange ?? throw new ArgumentNullException(nameof(customerChange));
    }

    public Task<HttpResponseData> AdHocSingleJobSyncAsync(HttpRequestData req, FunctionContext ctx)
        => _adHocSingle.ExecuteAsync(req, ctx);

    // NOTE: AdHocBatch_AllJobs requires DurableTaskClient. The dedicated function adapter uses IAdHocAllJobsUseCase directly.
    public Task<HttpResponseData> AdHocAllJobsAsync(HttpRequestData req, FunctionContext ctx)
        => CreateDedicatedEndpointResponseAsync(req);

    public Task<HttpResponseData> PostJobSyncAsync(HttpRequestData req, FunctionContext ctx)
        => _postJob.ExecuteAsync(req, ctx);

    public Task<HttpResponseData> CancelJobSyncAsync(HttpRequestData req, FunctionContext ctx)
        => _cancelJob.ExecuteAsync(req, ctx);

    public Task<HttpResponseData> CustomerChangeSyncAsync(HttpRequestData req, FunctionContext ctx)
        => _customerChange.ExecuteAsync(req, ctx);
}
