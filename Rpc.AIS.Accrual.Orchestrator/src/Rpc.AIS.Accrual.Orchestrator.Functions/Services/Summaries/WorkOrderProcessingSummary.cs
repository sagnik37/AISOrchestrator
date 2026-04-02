using System.Collections.Generic;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services.Summaries;

public sealed record WorkOrderProcessingSummary(
    int OpenWorkOrders,
    int DataIssueWorkOrders,
    int ValidatedWorkOrders,
    int NotValidatedWorkOrders,
    int CreatedInFscmSuccessfully,
    int ErrorWorkOrders,
    IReadOnlyList<WorkOrderErrorDetail> ErrorDetails,
    IReadOnlyList<WorkOrderDataIssueDetail> DataIssues);

public sealed record WorkOrderErrorDetail(
    string WorkOrderName,
    string? WorkOrderGuid,
    string ErrorReason,
    string ErrorDetails);

public sealed record WorkOrderDataIssueDetail(
    string WorkOrderName,
    string? WorkOrderGuid,
    string IssueReason,
    string IssueDetails);
