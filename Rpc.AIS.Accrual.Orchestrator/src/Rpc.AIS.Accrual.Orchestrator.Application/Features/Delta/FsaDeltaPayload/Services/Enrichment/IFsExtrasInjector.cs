namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.Enrichment;

using System;
using System.Collections.Generic;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload;

internal interface IFsExtrasInjector
{
    string InjectFsExtrasAndLogPerWoSummary(
        string payloadJson,
        IReadOnlyDictionary<Guid, FsLineExtras> extrasByLineGuid,
        string runId,
        string corr);
}
