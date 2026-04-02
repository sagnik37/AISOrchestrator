using System;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

/// <summary>
/// Central place for safe default results for WO payload validation.
/// </summary>
public static class WoPayloadValidationDefaults
{
    public static WoPayloadValidationResult EmptyResult()
        => new(
            "{}",
            Array.Empty<WoPayloadValidationFailure>(),
            0,
            0,
            "{}",
            Array.Empty<WoPayloadValidationFailure>(),
            0);
}
