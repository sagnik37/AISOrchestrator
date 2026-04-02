namespace Rpc.AIS.Accrual.Orchestrator.Core.Options;

/// <summary>
/// Defines processing mode values.
/// </summary>
public enum ProcessingMode
{
    PollerStaging = 1,
    InMemoryDurable = 2
}

/// <summary>
/// Provides processing options behavior.
/// </summary>
public sealed class ProcessingOptions
{
    public const string SectionName = "Processing";

    public ProcessingMode Mode { get; init; } = ProcessingMode.InMemoryDurable;

    public int MaxRecordsPerBatch { get; init; } = 50;
    public int TargetBatchBytes { get; init; } = 1_000_000;
    public int MaxParallelBatches { get; init; } = 3;

    public int DurablePostRetryAttempts { get; init; } = 5;
    public int DurablePostRetryFirstIntervalSeconds { get; init; } = 5;
    public double DurablePostRetryBackoffCoefficient { get; init; } = 2.0;
    public int DurablePostRetryMaxIntervalSeconds { get; init; } = 300;
}
