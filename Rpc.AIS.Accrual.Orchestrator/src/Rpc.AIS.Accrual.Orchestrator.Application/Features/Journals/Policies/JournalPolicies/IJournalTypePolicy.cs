namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.JournalPolicies;

using System;
using System.Collections.Generic;
using System.Text.Json;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

/// <summary>
/// Journal-type specific behavior (section key + local validations).
/// Intended to eliminate journal-type switches and keep the validation engine open for extension.
/// </summary>
public interface IJournalTypePolicy
{
    JournalType JournalType { get; }

    /// <summary>
    /// Section key used in the payload for this journal type (e.g., WOItemLines).
    /// </summary>
    string SectionKey { get; }

    /// <summary>
    /// Applies local (in-process) validations for a single journal line.
    /// Implementations should add failures to <paramref name="invalidFailures"/> only.
    /// </summary>
    void ValidateLocalLine(
        Guid woGuid,
        string? woNumber,
        Guid lineGuid,
        JsonElement line,
        List<WoPayloadValidationFailure> invalidFailures);
}
