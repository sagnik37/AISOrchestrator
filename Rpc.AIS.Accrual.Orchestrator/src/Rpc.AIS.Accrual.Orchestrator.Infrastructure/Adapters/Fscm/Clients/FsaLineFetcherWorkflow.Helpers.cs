// File: FsaLineFetcherWorkflow.Helpers.cs
//split helper responsibilities into partial files (behavior preserved).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

public sealed partial class FsaLineFetcherWorkflow
{


    private async Task<JsonDocument> AggregateByIdsAsync(
        string entitySetName,
        string idProperty,
        IReadOnlyList<Guid> ids,
        string select,
        string? expand,
        string? orderBy,
        int chunkSize,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entitySetName)) throw new ArgumentException("Entity set is required.", nameof(entitySetName));
        if (string.IsNullOrWhiteSpace(idProperty)) throw new ArgumentException("Id property is required.", nameof(idProperty));
        if (ids is null) throw new ArgumentNullException(nameof(ids));
        if (string.IsNullOrWhiteSpace(select)) throw new ArgumentException("Select is required.", nameof(select));
        if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));

        using var buffer = new MemoryStream();
        using var writer = new Utf8JsonWriter(buffer);

        writer.WriteStartObject();
        writer.WritePropertyName("value");
        writer.WriteStartArray();

        var pageSize = _opt.PageSize > 0 ? _opt.PageSize : 500;
        var maxPages = _opt.MaxPages > 0 ? _opt.MaxPages : 20;

        var total = 0;
        var chunkIndex = 0;
        var chunkTotal = (int)Math.Ceiling(ids.Count / (double)chunkSize);

        foreach (var chunk in Chunk(ids, chunkSize))
        {
            var swChunk = System.Diagnostics.Stopwatch.StartNew();
            chunkIndex++;
            ct.ThrowIfCancellationRequested();

            var filter = BuildOrGuidFilter(idProperty, chunk);

            var url =
                $"{entitySetName}" +
                $"?$select={select}" +
                (string.IsNullOrWhiteSpace(expand) ? "" : $"&$expand={expand}") +
                $"&$filter={filter}" +
                (string.IsNullOrWhiteSpace(orderBy) ? "" : $"&$orderby={orderBy}") +
                $"&$top={pageSize}";

            _log.LogInformation(
                "Dataverse fetch chunk {ChunkIndex}/{ChunkTotal}. EntitySet={EntitySet} IdProperty={IdProperty} IdCount={Count} Url={Url}",
                chunkIndex, chunkTotal, entitySetName, idProperty, chunk.Count, url);

            var rows = await CopyPagedValuesIntoAsync(
                initialUrl: url,
                maxPages: maxPages,
                writer: writer,
                logEntityName: entitySetName,
                ct: ct).ConfigureAwait(false);
            swChunk.Stop();
            total += rows;
            _log.LogInformation("Dataverse fetch chunk {ChunkIndex}/{ChunkTotal} complete. EntitySet={EntitySet} Rows={Rows} ElapsedMs={ElapsedMs}",
                chunkIndex, chunkTotal, entitySetName, rows, swChunk.ElapsedMilliseconds);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        buffer.Position = 0;

        _log.LogInformation("Dataverse fetch complete. EntitySet={EntitySet} TotalRows={TotalRows}", entitySetName, total);

        return JsonDocument.Parse(buffer);
    }

    private Task<JsonDocument> AggregatePagedAsync(
        string initialUrl,
        int maxPages,
        string logEntityName,
        CancellationToken ct)
    {
        return _reader.ReadAllPagesAsync(
            http: _http,
            initialRelativeUrl: initialUrl,
            maxPages: maxPages,
            logEntityName: logEntityName,
            ct: ct);
    }

    /// <summary>
    /// Executes copy doc value array into writer.
    /// </summary>
    private static int CopyDocValueArrayIntoWriter(JsonDocument doc, Utf8JsonWriter writer)
    {
        if (doc is null) return 0;
        if (!doc.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return 0;

        var c = 0;
        foreach (var el in arr.EnumerateArray())
        {
            el.WriteTo(writer);
            c++;
        }
        return c;
    }

    private async Task<int> CopyPagedValuesIntoAsync(
        string initialUrl,
        int maxPages,
        Utf8JsonWriter writer,
        string logEntityName,
        CancellationToken ct)
    {
        var doc = await _reader.ReadAllPagesAsync(
            http: _http,
            initialRelativeUrl: initialUrl,
            maxPages: maxPages,
            logEntityName: logEntityName,
            ct: ct).ConfigureAwait(false);

        return CopyDocValueArrayIntoWriter(doc, writer);
    }








    /// <summary>
    /// Executes try get value object.
    /// </summary>
    private static bool TryGetValueObject(JsonElement obj, string prop, out JsonElement valueObj)
    {
        valueObj = default;
        return obj.TryGetProperty(prop, out valueObj) && valueObj.ValueKind == JsonValueKind.Object;
    }

    /// <summary>
    /// Executes try string.
    /// </summary>
    private static string? TryString(JsonElement obj, string prop)
    {
        if (obj.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String)
            return p.GetString();
        return null;
    }

    /// <summary>
    /// Executes try guid.
    /// </summary>
    private static bool TryGuid(JsonElement row, string prop, out Guid id)
    {
        id = Guid.Empty;
        if (!row.TryGetProperty(prop, out var p)) return false;
        return p.ValueKind == JsonValueKind.String && Guid.TryParse(p.GetString(), out id);
    }

    /// <summary>
    /// Executes build or guid filter.
    /// </summary>
    private static string BuildOrGuidFilter(string property, IReadOnlyList<Guid> ids)
    {
        if (ids.Count == 1)
            return $"{property} eq {ToGuidLiteral(ids[0])}";

        var parts = ids.Select(id => $"{property} eq {ToGuidLiteral(id)}");
        return "(" + string.Join(" or ", parts) + ")";
    }

    private static string ToGuidLiteral(Guid id) => id.ToString("D");

    /// <summary>
    /// Executes chunk.
    /// </summary>
    private static IEnumerable<IReadOnlyList<Guid>> Chunk(IReadOnlyList<Guid> source, int chunkSize)
    {
        for (var i = 0; i < source.Count; i += chunkSize)
        {
            var size = Math.Min(chunkSize, source.Count - i);
            var chunk = new Guid[size];
            for (var j = 0; j < size; j++) chunk[j] = source[i + j];
            yield return chunk;
        }
    }

    private static JsonDocument EmptyValueDocument() => JsonDocument.Parse("{\"value\":[]}");

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken ct)
    {
        static void EnsureDataverseHeaders(HttpRequestMessage req, int preferMaxPageSize)
        {
            var preferParts = new List<string>
            {
                $"odata.maxpagesize={preferMaxPageSize}",
                "odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\""
            };

            if (req.Headers.Contains("Prefer"))
                req.Headers.Remove("Prefer");

            req.Headers.TryAddWithoutValidation("Prefer", string.Join(", ", preferParts));

            if (!req.Headers.Contains("OData-MaxVersion"))
                req.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
            if (!req.Headers.Contains("OData-Version"))
                req.Headers.TryAddWithoutValidation("OData-Version", "4.0");

            if (!req.Headers.Accept.Any(a => a.MediaType == "application/json"))
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        // Hard rule: never retry HTTP POST (non-idempotent).
        // Probe request method once (safe) to decide whether retries are allowed.
        var isPost = false;
        try
        {
            using var probe = requestFactory();
            isPost = probe.Method == HttpMethod.Post;
        }
        catch
        {
            // If probe fails, keep default behavior.
        }

        var maxAttempts = isPost ? 1 : MaxHttpAttempts;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            using var req = requestFactory();
            var preferMax = _opt.PreferMaxPageSize > 0 ? _opt.PreferMaxPageSize : PreferMaxPageSize;
            EnsureDataverseHeaders(req, preferMax);

            _log.LogInformation(
                "Dataverse request attempt {Attempt}/{Max}. Method={Method} Url={Url}",
                attempt, maxAttempts, req.Method, req.RequestUri?.ToString() ?? "<null>");

            HttpResponseMessage? resp = null;

            try
            {
                resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                if (resp.IsSuccessStatusCode)
                    return resp;

                if (IsRetryable(resp.StatusCode))
                {
                    var delay = GetRetryDelay(resp, attempt);
                    _log.LogWarning(
                        "Dataverse request retryable failure. Status={Status} Attempt={Attempt}/{Max} DelayMs={DelayMs}",
                        (int)resp.StatusCode, attempt, MaxHttpAttempts, (int)delay.TotalMilliseconds);

                    resp.Dispose();
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    continue;
                }

                var body = await SafeReadBodyAsync(resp, ct).ConfigureAwait(false);
                throw new HttpRequestException(
                    $"Dataverse request failed. Status={(int)resp.StatusCode} ({resp.StatusCode}). Body={body}");
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _log.LogWarning(ex, "Dataverse request failed on attempt {Attempt}/{Max}. Retrying...", attempt, maxAttempts);
                resp?.Dispose();
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, attempt * 2)), ct).ConfigureAwait(false);
            }
        }

        throw new HttpRequestException("Dataverse request failed after max attempts.");
    }

    /// <summary>
    /// Executes is retryable.
    /// </summary>
    private static bool IsRetryable(HttpStatusCode statusCode)
        => statusCode == (HttpStatusCode)429 || (int)statusCode >= 500;

    /// <summary>
    /// Executes get retry delay.
    /// </summary>
    private static TimeSpan GetRetryDelay(HttpResponseMessage resp, int attempt)
    {
        if (resp.Headers.TryGetValues("Retry-After", out var values))
        {
            var v = values.FirstOrDefault();
            if (int.TryParse(v, out var seconds) && seconds > 0)
                return TimeSpan.FromSeconds(seconds);
        }

        var delaySeconds = Math.Min(30, Math.Pow(2, attempt));
        return TimeSpan.FromSeconds(delaySeconds);
    }

    /// <summary>
    /// Executes safe read body async.
    /// </summary>
    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return "<body read failed>";
        }
    }
}
