using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Options;

/// <summary>
/// Provides ais diagnostics options adapter behavior.
/// </summary>
public sealed class AisDiagnosticsOptionsAdapter : IAisDiagnosticsOptions
{
    private readonly AisDiagnosticsOptions _o;

    public AisDiagnosticsOptionsAdapter(AisDiagnosticsOptions o)
        => _o = o ?? throw new ArgumentNullException(nameof(o));

    public bool LogPayloadBodies => _o.LogPayloadBodies;
    public bool LogMultiWoPayloadBody => _o.LogMultiWoPayloadBody;
    public bool IncludeDeltaReasonKey => _o.IncludeDeltaReasonKey;
    public int PayloadSnippetChars => _o.PayloadSnippetChars;
    public int PayloadChunkChars => _o.PayloadChunkChars;
}
