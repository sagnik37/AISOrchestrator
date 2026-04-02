using System.Text.Json.Nodes;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain;

/// <summary>
/// Carries status change request data.
/// </summary>
public sealed record StatusChangeRequest(
    string EntityName,
    string RecordId,
    string OldStatus,
    string NewStatus,
    string? Message,
    string? RunId,
    string? CorrelationId,
    JsonNode? Payload);

/// <summary>
/// Strongly-typed variant for status-specific payloads.
/// </summary>
public sealed record StatusChangeRequest<TPayload>(
    string EntityName,
    string RecordId,
    string OldStatus,
    string NewStatus,
    string? Message,
    string? RunId,
    string? CorrelationId,
    TPayload? Payload);
