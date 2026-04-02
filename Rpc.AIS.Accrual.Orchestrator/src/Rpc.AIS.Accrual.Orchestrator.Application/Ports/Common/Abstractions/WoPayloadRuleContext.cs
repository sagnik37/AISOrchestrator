using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;
using Rpc.AIS.Accrual.Orchestrator.Core.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.JournalPolicies;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Mutable context shared across WO payload validation rules.
/// </summary>
public sealed class WoPayloadRuleContext
{
    public WoPayloadRuleContext(
        RunContext runContext,
        JournalType journalType,
        string payloadJson,
        ILogger logger,
        PayloadValidationOptions options,
        IJournalTypePolicyResolver journalPolicyResolver,
        IFscmCustomValidationClient? fscmCustomValidator)
    {
        RunContext = runContext ?? throw new ArgumentNullException(nameof(runContext));
        JournalType = journalType;
        PayloadJson = payloadJson ?? throw new ArgumentNullException(nameof(payloadJson));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        JournalPolicyResolver = journalPolicyResolver ?? throw new ArgumentNullException(nameof(journalPolicyResolver));
        FscmCustomValidator = fscmCustomValidator;
    }

    public RunContext RunContext { get; }
    public JournalType JournalType { get; }
    public string PayloadJson { get; }

    public ILogger Logger { get; }
    public PayloadValidationOptions Options { get; }
    public IJournalTypePolicyResolver JournalPolicyResolver { get; }
    public IFscmCustomValidationClient? FscmCustomValidator { get; }

    // Parsed JSON (owned by the coordinator)
    public JsonDocument? Document { get; set; }
    public JsonElement WoList { get; set; }

    public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();

    // Working sets produced by rules
    public List<WoPayloadValidationFailure> InvalidFailures { get; } = new();
    public List<WoPayloadValidationFailure> RetryableFailures { get; } = new();

    public List<FilteredWorkOrder> ValidWorkOrders { get; } = new();
    public List<FilteredWorkOrder> RetryableWorkOrders { get; } = new();

    public int WorkOrdersBefore { get; set; }
    public int WorkOrdersAfter { get; set; }
    public int RetryableWorkOrdersAfter { get; set; }

    public string FilteredPayloadJson { get; set; } = "{}";
    public string RetryablePayloadJson { get; set; } = "{}";

    public WoPayloadValidationResult? Result { get; set; }
    public bool StopProcessing { get; set; }
    public FscmEndpointType Endpoint { get; set; } = FscmEndpointType.Unknown;

}
