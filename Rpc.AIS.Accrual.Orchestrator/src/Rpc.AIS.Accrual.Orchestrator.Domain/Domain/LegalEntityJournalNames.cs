namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain;

/// <summary>
/// Journal name identifiers configured per legal entity in FSCM.
/// Sourced from LegalEntityIntegrationParametersBaseIntParamTables.
/// </summary>
public sealed record LegalEntityJournalNames(
    string? ExpenseJournalNameId,
    string? HourJournalNameId,
    string? InventJournalNameId);
