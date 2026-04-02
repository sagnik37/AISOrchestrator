using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

/// <summary>
/// Small, deterministic parsing activities for V2 job operation orchestrations.
/// Kept separate to avoid non-deterministic JSON parsing in orchestrator code.
/// </summary>
public sealed class JobOperationsV2ParsingActivities
{
    private readonly ILogger<JobOperationsV2ParsingActivities> _log;

    public JobOperationsV2ParsingActivities(ILogger<JobOperationsV2ParsingActivities> log)
        => _log = log ?? throw new ArgumentNullException(nameof(log));

    public sealed record ExtractSubprojectGuidInputDto(
        string RunId,
        string CorrelationId,
        string? SourceSystem,
        Guid WorkOrderGuid,
        string RawRequestJson,
        string? DurableInstanceId = null);

    [Function(nameof(TryExtractSubprojectGuid))]
    public Task<Guid?> TryExtractSubprojectGuid([ActivityTrigger] ExtractSubprojectGuidInputDto input)
    {
        using var scope = LogScopes.BeginFunctionScope(_log, new LogScopeContext
        {
            Activity = nameof(TryExtractSubprojectGuid), // or nameof(SyncJobAttributesToProject), nameof(CustomerChangeExecute)
            Operation = nameof(TryExtractSubprojectGuid),
            Trigger = "Durable",
            RunId = input.RunId,
            CorrelationId = input.CorrelationId,
            SourceSystem = input.SourceSystem,
            WorkOrderGuid = input.WorkOrderGuid,
            DurableInstanceId = input.DurableInstanceId
        });


        if (string.IsNullOrWhiteSpace(input.RawRequestJson))
            return Task.FromResult<Guid?>(null);

        try
        {
            using var doc = JsonDocument.Parse(input.RawRequestJson);
            if (doc.RootElement.TryGetProperty("subprojectGuid", out var p))
            {
                var s = p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString();
                if (Guid.TryParse(s, out var g))
                    return Task.FromResult<Guid?>(g);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to parse subprojectGuid from request JSON.");
        }

        return Task.FromResult<Guid?>(null);
    }
}
