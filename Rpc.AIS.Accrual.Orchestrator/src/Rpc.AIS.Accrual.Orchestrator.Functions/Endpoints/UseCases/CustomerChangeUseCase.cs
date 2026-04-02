using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;
using Rpc.AIS.Accrual.Orchestrator.Functions.Services;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Customer Change use case.
/// Fully extracted (no shared endpoint handler dependency).
/// </summary>
public sealed class CustomerChangeUseCase : JobOperationsUseCaseBase, ICustomerChangeUseCase
{
    private readonly ICustomerChangeOrchestrator _customerChange;

    public CustomerChangeUseCase(
        ILogger<CustomerChangeUseCase> log,
        IAisLogger aisLogger,
        IAisDiagnosticsOptions diag,
        ICustomerChangeOrchestrator customerChange)
        : base(log, aisLogger, diag)
    {
        _customerChange = customerChange ?? throw new ArgumentNullException(nameof(customerChange));
    }

    public async Task<HttpResponseData> ExecuteAsync(HttpRequestData req, FunctionContext ctx)
    {
        var (runId, correlationId, _) = ReadContext(req);
        var sourceSystem = "FS";

        using var scope = LogScopes.BeginFunctionScope(_log, new LogScopeContext
        {
            Function = "CustomerChange",
            Operation = "CustomerChange",
            FlowName = "CustomerChange",
            Trigger = "Http",
            TriggerName = "CustomerChange",
            TriggerChannel = "Http",
            InitiatedBy = "FS",
            RunId = runId,
            CorrelationId = correlationId,
            SourceSystem = sourceSystem
        });

        var body = await ReadBodyAsync(req);

        await LogInboundPayloadAsync(runId, correlationId, "CustomerChange", body).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(body))
            return await BadRequestAsync(req, correlationId, runId, "Request body is required and must contain workOrderGuid and oldSubProjectId.");

        if (!TryParseFsJobOpsRequest(body, out var parsed, out var parseError))
            return await BadRequestAsync(req, correlationId, runId, parseError ?? "Invalid request body.");

        // FIX: prevents Company/DataAreaId resolution failure
        body = EnsureCompanyInRequestJson(body, parsed.Company, _log, runId, correlationId);

        // Prefer envelope-provided RunId/CorrelationId when present.
        runId = string.IsNullOrWhiteSpace(parsed.RunId) ? runId : parsed.RunId!;
        correlationId = string.IsNullOrWhiteSpace(parsed.CorrelationId) ? correlationId : parsed.CorrelationId!;
        var woGuid = parsed.WorkOrderGuid;

        _log.LogInformation("CUSTOMERCHANGE_REQUEST_ACCEPTED RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} TriggerName={TriggerName} InitiatedBy={InitiatedBy} Stage={Stage} WorkOrderGuid={WorkOrderGuid} Company={Company} SubProjectId={SubProjectId}",
            runId, correlationId, sourceSystem, "CustomerChange", "FS", "Accepted", woGuid, parsed.Company, parsed.SubProjectId);

        var runCtx = new RunContext(runId, DateTimeOffset.UtcNow, "CustomerChange", correlationId, sourceSystem, parsed.Company);

        using var woScope = LogScopes.BeginFunctionScope(_log, new LogScopeContext
        {
            Function = "CustomerChange",
            Operation = "CustomerChange",
            FlowName = "CustomerChange",
            Trigger = "Http",
            TriggerName = "CustomerChange",
            TriggerChannel = "Http",
            InitiatedBy = "FS",
            RunId = runId,
            CorrelationId = correlationId,
            SourceSystem = sourceSystem,
            WorkOrderGuid = woGuid,
            Stage = "Accepted"
        });

        try
        {
            _log.LogInformation("CUSTOMERCHANGE_STAGE_BEGIN RunId={RunId} CorrelationId={CorrelationId} TriggerName={TriggerName} SourceSystem={SourceSystem} Stage={Stage} WorkOrderGuid={WorkOrderGuid}",
                runId, correlationId, "CustomerChange", sourceSystem, "ExecuteBusinessFlow", woGuid);

            var result = await _customerChange.ExecuteAsync(runCtx, woGuid, body, ctx.CancellationToken);

            _log.LogInformation("CUSTOMERCHANGE_STAGE_END RunId={RunId} CorrelationId={CorrelationId} TriggerName={TriggerName} SourceSystem={SourceSystem} Stage={Stage} WorkOrderGuid={WorkOrderGuid} NewSubProjectId={NewSubProjectId}",
                runId, correlationId, "CustomerChange", sourceSystem, "ExecuteBusinessFlow", woGuid, result.NewSubProjectId);
            _log.LogInformation("CUSTOMERCHANGE_COMPLETED RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid} NewSubProjectId={NewSubProjectId}",
                runId, correlationId, woGuid, result.NewSubProjectId);

            return await OkAsync(req, correlationId, runId, new
            {
                runId,
                correlationId,
                sourceSystem,
                operation = "CustomerChange",
                workOrderGuid = woGuid,
                newSubProjectId = result.NewSubProjectId
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CustomerChange FAILED RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid}", runId, correlationId, woGuid);
            return await ServerErrorAsync(req, correlationId, runId, "CustomerChange failed.");
        }

    }

    private static string EnsureCompanyInRequestJson(string rawJson, string? parsedCompany, ILogger log, string? runId, string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(rawJson) || string.IsNullOrWhiteSpace(parsedCompany))
            return rawJson;

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream);

            if (root.TryGetProperty("_request", out var requestNode) &&
                requestNode.ValueKind == JsonValueKind.Object)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("_request");
                writer.WriteStartObject();

                bool hasCompanyAtRoot = false;

                foreach (var prop in requestNode.EnumerateObject())
                {
                    if (prop.NameEquals("WOList") && prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        writer.WritePropertyName("WOList");
                        writer.WriteStartArray();

                        foreach (var wo in prop.Value.EnumerateArray())
                        {
                            if (wo.ValueKind != JsonValueKind.Object)
                            {
                                wo.WriteTo(writer);
                                continue;
                            }

                            writer.WriteStartObject();
                            bool hasCompany = false;

                            foreach (var woProp in wo.EnumerateObject())
                            {
                                if (woProp.NameEquals("Company"))
                                    hasCompany = true;

                                woProp.WriteTo(writer);
                            }

                            if (!hasCompany)
                                writer.WriteString("Company", parsedCompany);

                            writer.WriteEndObject();
                        }

                        writer.WriteEndArray();
                    }
                    else if (prop.NameEquals("Company"))
                    {
                        hasCompanyAtRoot = true;
                        prop.WriteTo(writer);
                    }
                    else
                    {
                        prop.WriteTo(writer);
                    }
                }

                if (!hasCompanyAtRoot)
                    writer.WriteString("Company", parsedCompany);

                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteStartObject();
                bool hasCompany = false;

                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.NameEquals("Company") || prop.NameEquals("LegalEntity"))
                        hasCompany = true;

                    prop.WriteTo(writer);
                }

                if (!hasCompany)
                    writer.WriteString("Company", parsedCompany);

                writer.WriteEndObject();
            }

            writer.Flush();
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (Exception ex)
        {
            log.LogWarning(ex,
                "CustomerChangeUseCase: failed to normalize request JSON while ensuring Company. RunId={RunId} CorrelationId={CorrelationId}. Passing through raw JSON.",
                runId,
                correlationId);
            return rawJson;
        }
    }
}
