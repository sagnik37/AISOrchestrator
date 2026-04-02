namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.Enrichment;

internal interface IJournalDescriptionsStamper
{
    string StampJournalDescriptionsIntoPayload(string payloadJson, string action);
}
