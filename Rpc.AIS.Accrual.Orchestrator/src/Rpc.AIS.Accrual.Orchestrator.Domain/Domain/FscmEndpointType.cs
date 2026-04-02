namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain;

/// <summary>
/// Represents the FSCM endpoint being invoked.
/// Used to drive endpoint-specific local validation rules.
/// </summary>
public enum FscmEndpointType
{
    Unknown = 0,

    // Subproject
    SubProjectCreate,

    // Journals
    JournalValidate,
    JournalCreate,
    JournalPost,

    // Project
    InvoiceAttributesUpdate,
    ProjectStatusUpdate
}
