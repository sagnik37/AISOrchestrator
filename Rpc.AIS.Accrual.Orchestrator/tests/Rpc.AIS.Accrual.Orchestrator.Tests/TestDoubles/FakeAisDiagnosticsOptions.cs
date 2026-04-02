using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

namespace Rpc.AIS.Accrual.Orchestrator.Tests.TestDoubles;

public sealed class FakeAisDiagnosticsOptions : IAisDiagnosticsOptions
{
    public bool LogPayloadBodies { get; init; } = false;
    public bool LogMultiWoPayloadBody { get; init; } = false;
    public bool IncludeDeltaReasonKey { get; init; } = false;
    public int PayloadSnippetChars { get; init; } = 512;
    public int PayloadChunkChars { get; init; } = 4096;
}
