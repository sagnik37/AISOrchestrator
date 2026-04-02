using System;
using System.Collections.Generic;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload;

namespace Rpc.AIS.Accrual.Orchestrator.Application.Features.Delta.FsaDeltaPayload.Services.EnrichmentPipeline;

/// <summary>
/// Immutable input bundle for outbound delta payload enrichment.
/// Keeps the use case orchestration thin and enables OCP-friendly enrichment steps.
/// </summary>
public sealed record EnrichmentContext(
    string PayloadJson,
    string RunId,
    string CorrelationId,
    string Action,
    IReadOnlyDictionary<Guid, FsLineExtras>? ExtrasByLineGuid,
    IReadOnlyDictionary<Guid, string>? WoIdToCompanyName,
    IReadOnlyDictionary<string, LegalEntityJournalNames>? JournalNamesByCompany,
    IReadOnlyDictionary<Guid, string>? WoIdToSubProjectId,
    IReadOnlyDictionary<Guid, WoHeaderMappingFields>? WoIdToHeaderFields);
