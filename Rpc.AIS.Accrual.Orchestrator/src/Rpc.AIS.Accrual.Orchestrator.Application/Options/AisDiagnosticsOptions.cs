namespace Rpc.AIS.Accrual.Orchestrator.Core.Options;

/// <summary>
/// Provides ais diagnostics options behavior.
/// </summary>
public sealed class AisDiagnosticsOptions
{
    public bool LogPayloadBodies { get; init; }
    public bool LogMultiWoPayloadBody { get; init; }
    public bool IncludeDeltaReasonKey { get; init; }
    public int PayloadChunkChars { get; init; }

    public int PayloadSnippetChars { get; init; } = 4000;


}
