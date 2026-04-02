namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain;

/// <summary>
/// Carries get fsa delta payload input dto data.
/// </summary>
public sealed record GetFsaDeltaPayloadInputDto(
    string RunId,
    string CorrelationId,
    string TriggeredBy,
    string? WorkOrderGuid = null,
    string? DurableInstanceId = null);

/// <summary>
/// Carries get fsa delta payload result dto data.
/// </summary>
public sealed record GetFsaDeltaPayloadResultDto(
    string PayloadJson,
    string? ProductDeltaLinkAfter,
    string? ServiceDeltaLinkAfter,
    IReadOnlyList<string> WorkOrderNumbers);

// ---------------------------------------------------------------------
// Delta payload build (FS payload -> FSCM journal history -> delta payload)
// ---------------------------------------------------------------------

/// <summary>
/// Carries build delta payload from fscm history input dto data.
/// </summary>
public sealed record BuildDeltaPayloadFromFscmHistoryInputDto(
    string RunId,
    string CorrelationId,
    string TriggeredBy,
    string FsaPayloadJson,
    string? DurableInstanceId = null);

/// <summary>
/// Carries build delta payload from fscm history result dto data.
/// </summary>
public sealed record BuildDeltaPayloadFromFscmHistoryResultDto(
    string DeltaPayloadJson,
    int WorkOrdersInInput,
    int WorkOrdersInOutput,
    int DeltaLines,
    int ReverseLines,
    int RecreateLines);
