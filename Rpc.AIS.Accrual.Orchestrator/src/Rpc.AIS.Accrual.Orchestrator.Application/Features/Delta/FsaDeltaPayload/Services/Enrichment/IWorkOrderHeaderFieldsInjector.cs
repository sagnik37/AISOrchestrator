namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.Enrichment;

using System;
using System.Collections.Generic;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

internal interface IWorkOrderHeaderFieldsInjector
{
    string InjectWorkOrderHeaderFieldsIntoPayload(
        string payloadJson,
        IReadOnlyDictionary<Guid, WoHeaderMappingFields> woIdToHeaderFields);
}
