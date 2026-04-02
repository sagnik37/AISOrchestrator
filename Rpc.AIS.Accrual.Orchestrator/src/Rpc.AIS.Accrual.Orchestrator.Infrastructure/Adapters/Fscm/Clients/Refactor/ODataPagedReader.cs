using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;


public sealed class ODataPagedReader : IODataPagedReader
{
    private readonly ILogger<ODataPagedReader> _log;

    public ODataPagedReader(ILogger<ODataPagedReader> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<JsonDocument> ReadAllPagesAsync(
        HttpClient http,
        string initialRelativeUrl,
        int maxPages,
        string logEntityName,
        CancellationToken ct)
    {
        if (http is null) throw new ArgumentNullException(nameof(http));
        if (string.IsNullOrWhiteSpace(initialRelativeUrl)) throw new ArgumentException(nameof(initialRelativeUrl));
        if (maxPages <= 0) throw new ArgumentOutOfRangeException(nameof(maxPages));

        using var buffer = new MemoryStream();
        using var writer = new Utf8JsonWriter(buffer);

        writer.WriteStartObject();
        writer.WritePropertyName("value");
        writer.WriteStartArray();

        await CopyPagedValuesIntoAsync(http, initialRelativeUrl, maxPages, writer, logEntityName, ct).ConfigureAwait(false);

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        buffer.Position = 0;
        return JsonDocument.Parse(buffer);
    }

    private async Task<int> CopyPagedValuesIntoAsync(
        HttpClient http,
        string initialUrl,
        int maxPages,
        Utf8JsonWriter writer,
        string logEntityName,
        CancellationToken ct)
    {
        var url = initialUrl;
        var page = 0;
        var total = 0;

        while (!string.IsNullOrWhiteSpace(url) && page++ < maxPages)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            // Keep existing Prefer usage identical to the original behavior by not overriding headers here.

            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                // Let caller-level retry handle; just surface a useful log signal.
                _log.LogWarning("Dataverse throttling (429). Entity={Entity} Url={Url}", logEntityName, url);
            }

            resp.EnsureSuccessStatusCode();

            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            if (!doc.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                _log.LogWarning("Dataverse response does not contain 'value' array. Entity={Entity} Url={Url}", logEntityName, url);
                break;
            }

            foreach (var row in arr.EnumerateArray())
            {
                row.WriteTo(writer);
                total++;
            }

            url = doc.RootElement.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
        }

        return total;
    }
}
