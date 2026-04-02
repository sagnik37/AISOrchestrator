using System.Collections.Generic;
using System.Linq;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

/// <summary>
/// Provides ais validation post error builder behavior.
/// </summary>
public static class AisValidationPostErrorBuilder
{
    /// <summary>
    /// Executes build concise post errors.
    /// </summary>
    public static IReadOnlyList<PostError> BuildConcisePostErrors(IReadOnlyList<WoPayloadValidationFailure> failures, int max = 50)
    {
        if (failures is null || failures.Count == 0) return System.Array.Empty<PostError>();

        return failures.Take(max).Select(f => new PostError(
            Code: $"AIS_VALIDATION_{f.Code}",
            Message: $"WO={f.WorkOrderGuid} WO#={f.WorkOrderNumber ?? ""} Line={f.WorkOrderLineGuid?.ToString() ?? ""} {f.Message}",
            StagingId: null,
            JournalId: null,
            JournalDeleted: false,
            DeleteMessage: null)).ToList();
    }
}
