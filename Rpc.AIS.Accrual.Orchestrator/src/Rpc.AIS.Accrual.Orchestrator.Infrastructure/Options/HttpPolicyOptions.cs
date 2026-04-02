using System.ComponentModel.DataAnnotations;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

public sealed class HttpPolicyOptions
{
    public const string SectionName = "HttpPolicies";

    [Required]
    public CategoryOptions Dataverse { get; init; } = new();

    [Required]
    public CategoryOptions Fscm { get; init; } = new();

    public sealed class CategoryOptions
    {
        // Total timeout for one HTTP attempt (before retry)
        [Range(1, 600)]
        public int TimeoutSeconds { get; init; } = 60;

        // Max retries after initial attempt (so total attempts = 1 + Retries)
        [Range(0, 20)]
        public int Retries { get; init; } = 5;

        // Backoff cap
        [Range(1, 300)]
        public int MaxBackoffSeconds { get; init; } = 30;
    }
}
