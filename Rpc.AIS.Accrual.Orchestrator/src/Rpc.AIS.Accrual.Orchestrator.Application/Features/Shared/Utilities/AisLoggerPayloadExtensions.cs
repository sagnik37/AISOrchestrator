using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

/// <summary>
/// Production-safe helpers for logging large JSON payloads into App Insights traces.
/// - Logs sha256 + length always
/// - Logs a snippet (and optional chunks) when enabled
/// </summary>
public static class AisLoggerPayloadExtensions
{
    // App Insights / ILogger trace properties can still be truncated when a single structured string field is too large.
    // Keep each emitted snippet/chunk comfortably below those practical limits so the full payload remains reconstructable.
    private const int SafeMaxPropertyChars = 3000;
    private const int SafeMinSnippetChars = 256;
    private const int SingleEntryThresholdChars = SafeMaxPropertyChars;
    public static async Task LogJsonPayloadAsync(
        this IAisLogger logger,
        string runId,
        string step,
        string message,
        string payloadType,
        string workOrderGuid,
        string? workOrderNumber,
        string json,
        bool logBody,
        int snippetChars,
        int chunkChars,
        CancellationToken ct)
    {
        if (logger is null) throw new ArgumentNullException(nameof(logger));
        if (json is null) json = string.Empty;

        var len = json.Length;
        var sha = Sha256Hex(json);

        // Always log summary (safe, small)
        await logger.InfoAsync(runId, step, message, new
        {
            PayloadType = payloadType,
            WorkOrderGuid = workOrderGuid,
            WorkOrderNumber = workOrderNumber,
            PayloadLength = len,
            PayloadSha256 = sha
        }, ct).ConfigureAwait(false);

        if (!logBody || len == 0) return;

        // Keep single-entry/snippet/chunk sizes below practical per-property trace limits.
        var effectiveSnippetChars = Math.Clamp(snippetChars, SafeMinSnippetChars, SafeMaxPropertyChars);
        var effectiveChunkChars = chunkChars <= 0 ? 0 : Math.Clamp(chunkChars, 1, SafeMaxPropertyChars);

        if (len <= SingleEntryThresholdChars)
        {
            await logger.InfoAsync(runId, step, $"{message} (body)", new
            {
                PayloadType = payloadType,
                WorkOrderGuid = workOrderGuid,
                WorkOrderNumber = workOrderNumber,
                PayloadLength = len,
                PayloadSha256 = sha,
                IsChunked = false,
                JsonPayload = json
            }, ct).ConfigureAwait(false);
            return;
        }

        // Log a short snippet for quick inspection before emitting reconstructable chunks.
        var snippet = json.Length <= effectiveSnippetChars ? json : json.Substring(0, effectiveSnippetChars);
        await logger.InfoAsync(runId, step, $"{message} (snippet)", new
        {
            PayloadType = payloadType,
            WorkOrderGuid = workOrderGuid,
            WorkOrderNumber = workOrderNumber,
            PayloadLength = len,
            PayloadSha256 = sha,
            IsChunked = true,
            SnippetChars = snippet.Length,
            JsonSnippet = snippet
        }, ct).ConfigureAwait(false);

        // full payload, log in chunks to avoid trace truncation
        if (effectiveChunkChars <= 0) return;

        var totalChunks = (int)Math.Ceiling((double)len / effectiveChunkChars);
        for (int i = 0; i < totalChunks; i++)
        {
            var start = i * effectiveChunkChars;
            var size = Math.Min(effectiveChunkChars, len - start);
            var chunk = json.Substring(start, size);

            await logger.InfoAsync(runId, step, $"{message} (chunk)", new
            {
                PayloadType = payloadType,
                WorkOrderGuid = workOrderGuid,
                WorkOrderNumber = workOrderNumber,
                PayloadLength = len,
                PayloadSha256 = sha,
                IsChunked = true,
                ChunkIndex = i + 1,
                ChunkCount = totalChunks,
                ChunkChars = size,
                ChunkStart = start,
                ChunkEnd = start + size - 1,
                JsonChunk = chunk
            }, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Executes sha 256 hex.
    /// </summary>
    private static string Sha256Hex(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
