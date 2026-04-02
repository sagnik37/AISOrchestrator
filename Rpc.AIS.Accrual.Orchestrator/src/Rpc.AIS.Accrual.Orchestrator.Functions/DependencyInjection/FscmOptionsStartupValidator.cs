// File: src/Rpc.AIS.Accrual.Orchestrator.Functions/DependencyInjection/FscmOptionsStartupValidator.cs

using System;
using System.Linq;

using Microsoft.Extensions.Options;

using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.DependencyInjection;

internal sealed class FscmOptionsStartupValidator : IValidateOptions<FscmOptions>
{
    public ValidateOptionsResult Validate(string? name, FscmOptions options)
    {
        if (options is null) return ValidateOptionsResult.Fail("FSCM options are null.");

        static bool IsPlaceholder(string? v)
            => string.IsNullOrWhiteSpace(v) || v.Contains("<", StringComparison.OrdinalIgnoreCase);

        // Required endpoints (contract)
        var required = new (string Key, string? Value)[]
        {
            ("Endpoints:FscmBaseUrl", options.BaseUrl),
            ("Fscm:SubProjectPath", options.SubProjectPath),
            ("Fscm:JournalValidatePath", options.JournalValidatePath),
            ("Fscm:JournalCreatePath", options.JournalCreatePath),

            // validate the correct property
            ("Fscm:JournalPostPath", options.JournalPostPath),

            ("Fscm:UpdateInvoiceAttributesPath", options.UpdateInvoiceAttributesPath),
            ("Fscm:UpdateProjectStatusPath", options.UpdateProjectStatusPath),
        };

        var missing = required
            .Where(x => IsPlaceholder(x.Value))
            .Select(x => x.Key)
            .ToArray();

        if (missing.Length > 0)
            return ValidateOptionsResult.Fail("Missing or placeholder FSCM config: " + string.Join(", ", missing));

        // Distinctness check for multi-endpoint journal pipeline
        var journalEndpoints = new[]
        {
            options.JournalValidatePath,
            options.JournalCreatePath,

            // distinctness must use JournalPostPath (not CreatePath / not CustomPath)
            options.JournalPostPath,

            options.UpdateInvoiceAttributesPath,
        }.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

        if (journalEndpoints.Length != journalEndpoints.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            return ValidateOptionsResult.Fail("FSCM journal pipeline endpoints must be distinct.");

        return ValidateOptionsResult.Success;
    }
}
