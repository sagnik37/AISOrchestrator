namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.InvoiceAttributes;

/// <summary>
/// Minimal attribute definition returned by FSCM "attribute table" endpoint.
/// </summary>
public sealed record InvoiceAttributeDefinition(string AttributeName, string? Type = null, bool Active = true);
