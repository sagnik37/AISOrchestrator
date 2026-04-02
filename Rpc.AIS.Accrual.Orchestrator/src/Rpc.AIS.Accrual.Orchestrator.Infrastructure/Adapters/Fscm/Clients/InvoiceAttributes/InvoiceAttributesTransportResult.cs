using System.Net;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

public sealed record InvoiceAttributesTransportResult(HttpStatusCode StatusCode, string Body, long ElapsedMs);
