namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload;

public sealed class WoEnrichmentStats
{
    public string? WorkorderId { get; set; }
    public string? WorkorderGuidRaw { get; set; }
    public string? Company { get; set; }

    public int EnrichedLinesTotal { get; set; }
    public int EnrichedHourLines { get; set; }
    public int EnrichedExpLines { get; set; }
    public int EnrichedItemLines { get; set; }

    public int FilledCurrency { get; set; }
    public int FilledResourceId { get; set; }
    public int FilledWarehouse { get; set; }
    public int FilledSite { get; set; }
    public int FilledLineNum { get; set; }
    public int FilledOperationsDate { get; private set; }
    public void MarkFilledOperationsDate() => FilledOperationsDate++;

}
