namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.Enrichment;

using System;
using System.Collections.Generic;

internal interface ICompanyInjector
{
    string InjectCompanyIntoPayload(string payloadJson, IReadOnlyDictionary<Guid, string> woIdToCompanyName);
}
