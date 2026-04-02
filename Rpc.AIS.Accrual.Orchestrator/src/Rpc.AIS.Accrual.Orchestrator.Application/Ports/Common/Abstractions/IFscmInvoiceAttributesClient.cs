using System.Collections.Generic;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.InvoiceAttributes;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// FSCM custom endpoints for invoice attributes.
/// Used for runtime mapping (definitions), current value snapshot, and updates.
/// </summary>
public interface IFscmInvoiceAttributesClient
{
    Task<IReadOnlyList<InvoiceAttributeDefinition>> GetDefinitionsAsync(
        RunContext ctx,
        string company,
        string subProjectId,
        CancellationToken ct);

    Task<IReadOnlyList<InvoiceAttributePair>> GetCurrentValuesAsync(
        RunContext ctx,
        string company,
        string subProjectId,
        IReadOnlyList<string> attributeNames,
        CancellationToken ct);

    Task<FscmInvoiceAttributesUpdateResult> UpdateAsync(
        RunContext ctx,
        string company,
        string subProjectId,
        Guid workOrderGuid,
        string workOrderId,
        string? countryRegionId,
        string? county,
        string? state,
        string? dimensionDisplayValue,
        string? fsaTaxabilityType,
        string? fsaWellAge,
        string? fsaWorkType,
        IReadOnlyDictionary<string, object?>? additionalHeaderFields,
        IReadOnlyList<InvoiceAttributePair> updates,
        CancellationToken ct);
}

public sealed record FscmInvoiceAttributesUpdateResult(bool IsSuccess, int HttpStatus, string? Body);
