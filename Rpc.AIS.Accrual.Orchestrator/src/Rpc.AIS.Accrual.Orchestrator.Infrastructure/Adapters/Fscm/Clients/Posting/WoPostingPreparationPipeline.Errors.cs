using System;
using System.Collections.Generic;
using System.Linq;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;

public sealed partial class WoPostingPreparationPipeline
{
    private static (int RetryableWorkOrders, int RetryableLines) CountRetryable(
        IReadOnlyList<WoPayloadValidationFailure>? failures)
    {
        if (failures is null || failures.Count == 0)
            return (0, 0);

        var retryableWorkOrders = failures
            .Select(f => f.WorkOrderNumber)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return (retryableWorkOrders, failures.Count);
    }

    private static List<PostError> ToPostErrors(IReadOnlyList<WoPayloadValidationFailure>? failures)
    {
        if (failures is null || failures.Count == 0)
            return new List<PostError>(0);

        var list = new List<PostError>(failures.Count);

        foreach (var f in failures)
        {
            var woId = f.WorkOrderNumber ?? string.Empty;
            string? lineGuid = f.WorkOrderLineGuid.HasValue ? $"{{{f.WorkOrderLineGuid.Value}}}" : null;

            list.Add(new PostError(
                woId,
                f.Message ?? "Validation failure",
                lineGuid,
                null,
                false,
                null));
        }

        return list;
    }
}
