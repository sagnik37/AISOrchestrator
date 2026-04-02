namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain;

/// <summary>
/// Canonical request model for FSCM SubProject creation.
/// This model represents the inner object of the FSCM contract envelope: { "_request": { ... } }.
/// </summary>
public sealed record SubProjectCreateRequest(
    string DataAreaId,
    string ParentProjectId,
    string ProjectName,
    string? CustomerReference,
    string? InvoiceNotes,
    string? ActualStartDate,
    string? ActualEndDate,
    string? AddressName,
    string? Street,
    string? City,
    string? State,
    string? County,
    string? CountryRegionId,
    string? WellLocale,
    string? WellName,
    string? WellNumber,
    int? ProjectStatus)
{
    /// <summary>
    /// Optional Work Order Id to be sent as 'WorkOrderId' into FSCM contract when present.
    /// Used by subproject creation flows that previously overloaded ProjectName with the work order id.
    /// </summary>
    public string? WorkOrderId { get; init; }

    /// <summary>
    /// Optional Work Order GUID (Field Service / Customer Change).
    /// Serialized as 'WorkOrderGUID' into FSCM contract when present.
    /// Prefer brace format "{GUID}".
    /// </summary>
    public string? WorkOrderGuid { get; init; }

    /// <summary>
    /// Optional FSCM create control flag. Serialized as 'IsFSAProject' when present.
    /// </summary>
    public int? IsFsaProject { get; init; }

    /// <summary>
    /// Optional FSCM create control. Serialized as 'ProjectStatus' when present.
    /// </summary>
    public int? ProjectStatus { get; init; }

    public object? LegalEntity { get; internal set; }
}

/// <summary>
/// Carries sub project create result data.
/// </summary>
public sealed record SubProjectCreateResult(
    bool IsSuccess,
    string? parmSubProjectId,
    string? Message,
    IReadOnlyList<SubProjectError> Errors);

/// <summary>
/// Carries sub project error data.
/// </summary>
public sealed record SubProjectError(
    string Code,
    string Message);
