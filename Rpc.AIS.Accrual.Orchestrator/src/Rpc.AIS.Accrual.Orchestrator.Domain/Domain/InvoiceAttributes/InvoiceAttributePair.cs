namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.InvoiceAttributes;

/// <summary>
/// Name/value pair contract used by FSCM custom endpoints.
/// AttributeName must be the FSCM attribute name.
/// </summary>
public sealed record InvoiceAttributePair(string AttributeName, string? AttributeValue);
