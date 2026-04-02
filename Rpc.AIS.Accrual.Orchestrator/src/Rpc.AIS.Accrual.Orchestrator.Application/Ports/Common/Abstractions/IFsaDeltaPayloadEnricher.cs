using System;
using System.Collections.Generic;
using System.Text.Json;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Enriches outbound delta payload JSON with FS-only fields (company/subproject, currency/resource/warehouse/site/lineNum/projectCategory).
/// </summary>
public interface IFsaDeltaPayloadEnricher
{
    string InjectFsExtrasAndLogPerWoSummary(string payloadJson, Dictionary<Guid, FsLineExtras> extrasByLineGuid, string runId, string corr);

    string InjectCompanyIntoPayload(string payloadJson, IReadOnlyDictionary<Guid, string> woIdToCompanyName);

    string InjectSubProjectIdIntoPayload(string payloadJson, IReadOnlyDictionary<Guid, string> woIdToSubProjectId);

    /// <summary>
    /// Injects mapping-only Work Order header fields fetched from Dataverse into WOList entries.
    /// Not used for delta calculation.
    /// </summary>
    string InjectWorkOrderHeaderFieldsIntoPayload(
        string payloadJson,
        IReadOnlyDictionary<Guid, WoHeaderMappingFields> woIdToHeaderFields);

    /// <summary>
    /// Adds JournalName at the journal header level (WOItemLines/WOExpLines/WOHourLines) based on legal entity settings in FSCM.
    /// </summary>
    string InjectJournalNamesIntoPayload(
        string payloadJson,
        IReadOnlyDictionary<string, LegalEntityJournalNames> journalNamesByCompany);

    /// <summary>
    /// Recomputes and stamps JournalDescription/JournalLineDescription using the final header values
    /// (WorkOrderID + SubProjectId) after all enrichment has completed.
    /// Action must be one of Post/Reverse/Recreate/Cancel.
    /// </summary>
    string StampJournalDescriptionsIntoPayload(string payloadJson, string action);
}

