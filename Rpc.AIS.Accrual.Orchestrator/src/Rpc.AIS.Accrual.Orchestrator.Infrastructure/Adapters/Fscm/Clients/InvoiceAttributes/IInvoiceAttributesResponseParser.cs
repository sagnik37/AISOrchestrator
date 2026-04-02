using System.Collections.Generic;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.InvoiceAttributes;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

public interface IInvoiceAttributesResponseParser
{
    IReadOnlyList<InvoiceAttributeDefinition> ParseDefinitions(string body);

    IReadOnlyList<InvoiceAttributePair> ParseCurrentValues(string body);
}
