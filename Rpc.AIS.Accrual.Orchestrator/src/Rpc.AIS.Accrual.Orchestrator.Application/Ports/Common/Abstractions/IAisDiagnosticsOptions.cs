namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Defines i ais diagnostics options behavior.
/// </summary>
public interface IAisDiagnosticsOptions
{
    bool LogPayloadBodies { get; }
    bool LogMultiWoPayloadBody { get; }
    bool IncludeDeltaReasonKey { get; }
    int PayloadSnippetChars { get; }
    int PayloadChunkChars { get; }
}
