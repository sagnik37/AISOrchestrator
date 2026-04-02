using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Functions;

public sealed class AdHocAllStatusUseCase : JobOperationsUseCaseBase, IAdHocAllStatusUseCase
{
    public AdHocAllStatusUseCase(
        ILogger<AdHocAllStatusUseCase> log,
        IAisLogger aisLogger,
        IAisDiagnosticsOptions diag)
        : base(log, aisLogger, diag)
    {
    }

    public async Task<HttpResponseData> ExecuteAsync(HttpRequestData req, DurableTaskClient client, FunctionContext ctx)
    {
        var query = ParseQuery(req.Url.Query);
        query.TryGetValue("instanceId", out var instanceId);
        query.TryGetValue("runId", out var runId);
        query.TryGetValue("correlationId", out var correlationId);
        query.TryGetValue("sourceSystem", out var sourceSystem);

        correlationId ??= string.Empty;
        sourceSystem ??= string.Empty;

        if (string.IsNullOrWhiteSpace(instanceId) && !string.IsNullOrWhiteSpace(runId))
            instanceId = $"{runId}-adhoc-all";

        using var scope = LogScopes.BeginFunctionScope(_log, new LogScopeContext
        {
            Function = "AdHocBatch_AllJobs_Status",
            Operation = "AdHocBatch_AllJobs_Status",
            Trigger = "Http",
            RunId = runId,
            CorrelationId = correlationId,
            SourceSystem = sourceSystem,
            DurableInstanceId = instanceId
        });

        if (string.IsNullOrWhiteSpace(instanceId))
            return await BadRequestAsync(req, correlationId, runId ?? string.Empty, "Query string must include instanceId or runId.");

        var state = await client.GetInstanceAsync(instanceId, ctx.CancellationToken);
        if (state is null)
        {
            _log.LogWarning(
                "ADHOC_ALL_STATUS_NOT_FOUND RunId={RunId} CorrelationId={CorrelationId} InstanceId={InstanceId}",
                runId,
                correlationId,
                instanceId);

            return await NotFoundAsync(req, correlationId, runId ?? string.Empty, new AdHocAllStatusResponse(
                Status: "NotFound",
                InstanceId: instanceId,
                RuntimeStatus: null,
                CreatedAtUtc: null,
                LastUpdatedAtUtc: null,
                SerializedInput: null,
                SerializedOutput: null,
                FailureDetails: null,
                Exists: false));
        }

        var response = new AdHocAllStatusResponse(
            Status: "OK",
            InstanceId: instanceId,
            RuntimeStatus: state.RuntimeStatus.ToString(),
            CreatedAtUtc: TryGetStringProperty(state, "CreatedAt"),
            LastUpdatedAtUtc: TryGetStringProperty(state, "LastUpdatedAt"),
            SerializedInput: TryGetStringProperty(state, "SerializedInput") ?? TryGetStringProperty(state, "Input"),
            SerializedOutput: TryGetStringProperty(state, "SerializedOutput") ?? TryGetStringProperty(state, "Output"),
            FailureDetails: TryGetStringProperty(state, "FailureDetails") ?? TryGetStringProperty(state, "Failure"),
            Exists: true);

        _log.LogInformation(
            "ADHOC_ALL_STATUS_READ RunId={RunId} CorrelationId={CorrelationId} InstanceId={InstanceId} RuntimeStatus={RuntimeStatus}",
            runId,
            correlationId,
            instanceId,
            state.RuntimeStatus);

        return await OkAsync(req, correlationId, runId ?? string.Empty, response);
    }

    private static Dictionary<string, string> ParseQuery(string queryString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(queryString))
            return result;

        var query = queryString[0] == '?' ? queryString[1..] : queryString;
        var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            var idx = part.IndexOf('=');
            if (idx < 0)
            {
                result[Uri.UnescapeDataString(part)] = string.Empty;
                continue;
            }

            var key = Uri.UnescapeDataString(part[..idx]);
            var value = Uri.UnescapeDataString(part[(idx + 1)..]);
            result[key] = value;
        }

        return result;
    }

    private static string? TryGetStringProperty(object instance, string propertyName)
    {
        var prop = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop is null)
            return null;

        var value = prop.GetValue(instance);
        return value switch
        {
            null => null,
            DateTimeOffset dto => dto.UtcDateTime.ToString("O"),
            DateTime dt => dt.ToUniversalTime().ToString("O"),
            _ => value.ToString()
        };
    }
}
