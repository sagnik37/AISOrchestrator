using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;
using Rpc.AIS.Accrual.Orchestrator.Core.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.JournalPolicies;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.WoPayloadValidationRules;

public sealed class WoPayloadValidationPipelineEngine : IWoPayloadValidationEngine
{
    private readonly IEnumerable<IWoPayloadRule> _rules;
    private readonly ILogger<WoPayloadValidationPipelineEngine> _logger;
    private readonly PayloadValidationOptions _options;
    private readonly IJournalTypePolicyResolver _journalPolicyResolver;
    private readonly IFscmCustomValidationClient? _fscmCustomValidator;

    public WoPayloadValidationPipelineEngine(
        IEnumerable<IWoPayloadRule> rules,
        ILogger<WoPayloadValidationPipelineEngine> logger,
        IOptions<PayloadValidationOptions> options,
        IJournalTypePolicyResolver journalPolicyResolver,
        IFscmCustomValidationClient? fscmCustomValidator = null)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new PayloadValidationOptions();
        _journalPolicyResolver = journalPolicyResolver ?? throw new ArgumentNullException(nameof(journalPolicyResolver));
        _fscmCustomValidator = fscmCustomValidator;
    }

    public async Task<WoPayloadValidationResult> ValidateAndFilterAsync(
        RunContext context,
        JournalType journalType,
        string woPayloadJson,
        CancellationToken ct)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        JsonDocument? document = null;
        var workOrdersBefore = 0;

        try
        {
            if (!string.IsNullOrWhiteSpace(woPayloadJson))
            {
                document = JsonDocument.Parse(woPayloadJson);

                if (document.RootElement.TryGetProperty("_request", out var request) &&
                    request.ValueKind == JsonValueKind.Object &&
                    request.TryGetProperty("WOList", out var woList) &&
                    woList.ValueKind == JsonValueKind.Array)
                {
                    workOrdersBefore = woList.GetArrayLength();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "WO payload JSON parse failed before validation pipeline. RunId={RunId} CorrelationId={CorrelationId} JournalType={JournalType}",
                context.RunId,
                context.CorrelationId,
                journalType);

            return new WoPayloadValidationResult(
                filteredPayloadJson: "{}",
                failures: Array.Empty<WoPayloadValidationFailure>(),
                workOrdersBefore: 0,
                workOrdersAfter: 0);
        }

        if (document is null || workOrdersBefore == 0)
        {
            return new WoPayloadValidationResult(
                filteredPayloadJson: "{}",
                failures: Array.Empty<WoPayloadValidationFailure>(),
                workOrdersBefore: workOrdersBefore,
                workOrdersAfter: 0);
        }

        var ruleContext = new WoPayloadRuleContext(
            runContext: context,
            journalType: journalType,
            payloadJson: woPayloadJson,
            logger: _logger,
            options: _options,
            journalPolicyResolver: _journalPolicyResolver,
            fscmCustomValidator: _fscmCustomValidator)
        {
            Document = document,
            WorkOrdersBefore = workOrdersBefore,
            Endpoint = FscmEndpointType.JournalValidate
        };

        foreach (var rule in _rules)
        {
            await rule.ApplyAsync(ruleContext, ct).ConfigureAwait(false);

            if (ruleContext.StopProcessing)
                break;
        }

        return ruleContext.Result ?? new WoPayloadValidationResult(
            filteredPayloadJson: "{}",
            failures: Array.Empty<WoPayloadValidationFailure>(),
            workOrdersBefore: workOrdersBefore,
            workOrdersAfter: 0);
    }
}
