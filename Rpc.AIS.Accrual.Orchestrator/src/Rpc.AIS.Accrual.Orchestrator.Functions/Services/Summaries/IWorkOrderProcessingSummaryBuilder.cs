using System.Collections.Generic;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services.Summaries;

public interface IWorkOrderProcessingSummaryBuilder
{
    WorkOrderProcessingSummary Build(string woPayloadJson, IReadOnlyList<PostResult> postResults, IReadOnlyList<string>? generalErrors);
}
