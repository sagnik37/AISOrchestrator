using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

public interface IInvoiceAttributesHttpTransport
{
    Task<InvoiceAttributesTransportResult> PostJsonAsync(
        HttpClient http,
        RunContext ctx,
        string url,
        string payloadJson,
        string operation,
        CancellationToken ct);
}
