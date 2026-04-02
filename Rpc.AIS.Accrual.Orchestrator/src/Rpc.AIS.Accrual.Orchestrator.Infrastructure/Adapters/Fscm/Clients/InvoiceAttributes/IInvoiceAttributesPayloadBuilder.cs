using System;
using System.Collections.Generic;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.InvoiceAttributes;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

public interface IInvoiceAttributesPayloadBuilder
{
    string BuildUrl(string baseUrl, string path);

    string BuildDefinitionsPayload(string company, string subProjectId);

    string BuildCurrentValuesPayload(string company, string subProjectId, IReadOnlyList<string> attributeNames);

    string BuildUpdatePayload(
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
        IReadOnlyList<InvoiceAttributePair> updates);
}
