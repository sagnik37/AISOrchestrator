using System;
using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Defines i wo delta payload service behavior.
/// </summary>
public interface IWoDeltaPayloadService
{
    Task<WoDeltaPayloadBuildResult> BuildDeltaPayloadAsync(
        RunContext context,
        string fsaWoPayloadJson,
        DateTime todayUtc,
        CancellationToken ct);
}

/// <summary>
/// Carries wo delta payload build result data.
/// </summary>
public sealed record WoDeltaPayloadBuildResult(
    string DeltaPayloadJson,
    int WorkOrdersInInput,
    int WorkOrdersInOutput,
    int TotalDeltaLines,
    int TotalReverseLines,
    int TotalRecreateLines
);
