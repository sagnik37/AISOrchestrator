using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure;

/// <summary>
/// Provides telemetry behavior.
/// </summary>
public sealed class Telemetry : Rpc.AIS.Accrual.Orchestrator.Core.Abstractions.ITelemetry
{
    private readonly ILogger<Telemetry> _log;

    public Telemetry(ILogger<Telemetry> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Executes log json.
    /// </summary>
    public void LogJson(string eventName, string runId, string correlationId, string? workOrderNumber, string json, int chunkSize = 7000)
    {
        var hash = Sha256Hex(json);
        var total = json.Length;
        var chunks = (int)Math.Ceiling(total / (double)chunkSize);

        for (int i = 0; i < chunks; i++)
        {
            var part = json.Substring(i * chunkSize, Math.Min(chunkSize, total - i * chunkSize));
            _log.LogInformation(
                "{EventName} JSON_CHUNK RunId={RunId} CorrelationId={CorrelationId} WorkOrderNumber={WorkOrderNumber} JsonHash={JsonHash} ChunkIndex={ChunkIndex} ChunkCount={ChunkCount} Chunk={Chunk}",
                eventName, runId, correlationId, workOrderNumber, hash, i, chunks, part);
        }
    }

    /// <summary>
    /// Executes sha 256 hex.
    /// </summary>
    public static string Sha256Hex(string s)
    {
        using var sha = SHA256.Create();
        var b = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(b).ToLowerInvariant();
    }
}
