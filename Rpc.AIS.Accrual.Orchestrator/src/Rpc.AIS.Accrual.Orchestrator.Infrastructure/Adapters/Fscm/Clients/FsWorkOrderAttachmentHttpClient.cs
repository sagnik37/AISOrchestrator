using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Reads Work Order attachments from Dataverse table rpc_attachmentuploadactivities.
/// </summary>
public sealed class FsWorkOrderAttachmentHttpClient : IFsWorkOrderAttachmentClient
{
    private const string AttachmentsEntitySet = "rpc_attachmentuploadactivities";
    private const string SelectWithoutConfidentiality = "rpc_filename,rpc_bloburl";
    private const string SelectWithConfidentiality = "rpc_filename,rpc_bloburl,rpc_confidentiality";
    private const string ExternalConfidentiality = "External";
    private const string InternalConfidentiality = "Internal";

    private readonly HttpClient _http;
    private readonly FsOptions _opt;
    private readonly ILogger<FsWorkOrderAttachmentHttpClient> _log;

    public FsWorkOrderAttachmentHttpClient(HttpClient http, IOptions<FsOptions> opt, ILogger<FsWorkOrderAttachmentHttpClient> log)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _opt = opt?.Value ?? throw new ArgumentNullException(nameof(opt));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<IReadOnlyList<WorkOrderAttachment>> GetForWorkOrderAsync(RunContext ctx, Guid workOrderGuid, CancellationToken ct)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        if (workOrderGuid == Guid.Empty) throw new ArgumentException("WorkOrderGuid is required.", nameof(workOrderGuid));

        var guidLiteral = workOrderGuid.ToString("D");
        var candidates = BuildCandidateQueries(guidLiteral);

        foreach (var candidate in candidates)
        {
            var result = await TryReadAttachmentsAsync(ctx, candidate, ct).ConfigureAwait(false);
            if (result.Status == AttachmentReadStatus.SchemaMismatch)
                continue;

            if (result.Status == AttachmentReadStatus.Failed)
            {
                _log.LogWarning(
                    "FS_ATTACHMENTS query failed. Status={Status} RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid} Query={Query}",
                    result.HttpStatus, ctx.RunId, ctx.CorrelationId, workOrderGuid, candidate);
                return Array.Empty<WorkOrderAttachment>();
            }

            if (result.Attachments.Count == 0)
            {
                _log.LogInformation(
                    "FS_ATTACHMENTS no eligible attachments found. RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid} HasConfidentialityField={HasConfidentialityField} SyncInternalDocumentsToFscm={SyncInternalDocumentsToFscm}",
                    ctx.RunId, ctx.CorrelationId, workOrderGuid, result.HasConfidentialityField, _opt.SyncInternalDocumentsToFscm);
                return Array.Empty<WorkOrderAttachment>();
            }

            _log.LogInformation(
                "FS_ATTACHMENTS resolved {Count} eligible attachment(s). RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid} HasConfidentialityField={HasConfidentialityField} SyncInternalDocumentsToFscm={SyncInternalDocumentsToFscm}",
                result.Attachments.Count, ctx.RunId, ctx.CorrelationId, workOrderGuid, result.HasConfidentialityField, _opt.SyncInternalDocumentsToFscm);

            return result.Attachments;
        }

        _log.LogWarning(
            "FS_ATTACHMENTS schema mismatch: none of the known filter fields worked. RunId={RunId} CorrelationId={CorrelationId} WorkOrderGuid={WorkOrderGuid}",
            ctx.RunId, ctx.CorrelationId, workOrderGuid);

        return Array.Empty<WorkOrderAttachment>();
    }

    private static IReadOnlyList<string> BuildCandidateQueries(string guidLiteral)
        => new[]
        {
            BuildRelativeUrl("_regardingobjectid_value", guidLiteral, includeConfidentiality: true),
            BuildRelativeUrl("_rpc_workorder_value", guidLiteral, includeConfidentiality: true),
            BuildRelativeUrl("rpc_workorderguid", guidLiteral, includeConfidentiality: true),
        };

    private static string BuildRelativeUrl(string filterField, string guidLiteral, bool includeConfidentiality)
    {
        var select = includeConfidentiality ? SelectWithConfidentiality : SelectWithoutConfidentiality;
        return $"{AttachmentsEntitySet}?$select={select}&$filter={filterField} eq {guidLiteral}";
    }

    private async Task<AttachmentReadResult> TryReadAttachmentsAsync(RunContext ctx, string candidateWithConfidentiality, CancellationToken ct)
    {
        var withConfidentiality = await TryGetAsync(ctx, candidateWithConfidentiality, ct).ConfigureAwait(false);
        if (withConfidentiality.Ok)
        {
            var parsed = Parse(withConfidentiality.Body, hasConfidentialityField: true);
            return new AttachmentReadResult(AttachmentReadStatus.Success, withConfidentiality.Status, FilterAttachments(parsed, hasConfidentialityField: true), true);
        }

        if (withConfidentiality.Status != (int)HttpStatusCode.BadRequest)
            return new AttachmentReadResult(AttachmentReadStatus.Failed, withConfidentiality.Status, Array.Empty<WorkOrderAttachment>(), HasConfidentialityField: false);

        var fallbackUrl = candidateWithConfidentiality.Replace($"$select={SelectWithConfidentiality}", $"$select={SelectWithoutConfidentiality}", StringComparison.Ordinal);
        var withoutConfidentiality = await TryGetAsync(ctx, fallbackUrl, ct).ConfigureAwait(false);
        if (withoutConfidentiality.Ok)
        {
            _log.LogInformation(
                "FS_ATTACHMENTS rpc_confidentiality field is unavailable; syncing all attachments for this source query. RunId={RunId} CorrelationId={CorrelationId}",
                ctx.RunId, ctx.CorrelationId);

            var parsed = Parse(withoutConfidentiality.Body, hasConfidentialityField: false);
            var attachments = parsed.Select(static a => a.Attachment).ToList();
            return new AttachmentReadResult(AttachmentReadStatus.Success, withoutConfidentiality.Status, attachments, HasConfidentialityField: false);
        }

        if (withoutConfidentiality.Status == (int)HttpStatusCode.BadRequest)
            return new AttachmentReadResult(AttachmentReadStatus.SchemaMismatch, withoutConfidentiality.Status, Array.Empty<WorkOrderAttachment>(), HasConfidentialityField: false);

        return new AttachmentReadResult(AttachmentReadStatus.Failed, withoutConfidentiality.Status, Array.Empty<WorkOrderAttachment>(), HasConfidentialityField: false);
    }

    private async Task<(bool Ok, int Status, string Body)> TryGetAsync(RunContext ctx, string relativeUrl, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        msg.Headers.TryAddWithoutValidation("Accept", "application/json");
        msg.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
        msg.Headers.TryAddWithoutValidation("OData-Version", "4.0");
        msg.Headers.TryAddWithoutValidation("Prefer", $"odata.maxpagesize={Math.Max(1, _opt.PreferMaxPageSize)}");

        if (!string.IsNullOrWhiteSpace(ctx.RunId)) msg.Headers.TryAddWithoutValidation("x-run-id", ctx.RunId);
        if (!string.IsNullOrWhiteSpace(ctx.CorrelationId)) msg.Headers.TryAddWithoutValidation("x-correlation-id", ctx.CorrelationId);

        using var resp = await _http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        var body = resp.Content is null ? string.Empty : await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        var ok = (int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299;
        return (ok, (int)resp.StatusCode, body ?? string.Empty);
    }

    private IReadOnlyList<WorkOrderAttachment> FilterAttachments(IReadOnlyList<ParsedAttachment> attachments, bool hasConfidentialityField)
    {
        if (!hasConfidentialityField)
            return attachments.Select(static a => a.Attachment).ToList();

        var allowedValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ExternalConfidentiality
        };

        if (_opt.SyncInternalDocumentsToFscm)
            allowedValues.Add(InternalConfidentiality);

        return attachments
            .Where(a => allowedValues.Contains(a.Confidentiality ?? string.Empty))
            .Select(static a => a.Attachment)
            .ToList();
    }

    private static List<ParsedAttachment> Parse(string json, bool hasConfidentialityField)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<ParsedAttachment>();

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
            return new List<ParsedAttachment>();

        var list = new List<ParsedAttachment>();
        foreach (var item in value.EnumerateArray())
        {
            var fileName = item.TryGetProperty("rpc_filename", out var fn) && fn.ValueKind == JsonValueKind.String ? fn.GetString() : null;
            var url = item.TryGetProperty("rpc_bloburl", out var bu) && bu.ValueKind == JsonValueKind.String ? bu.GetString() : null;

            if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(url))
                continue;

            var confidentiality = hasConfidentialityField &&
                                  item.TryGetProperty("rpc_confidentiality", out var conf) &&
                                  conf.ValueKind == JsonValueKind.String
                ? conf.GetString()?.Trim()
                : null;

            list.Add(new ParsedAttachment(new WorkOrderAttachment(fileName!, url!, confidentiality), confidentiality));
        }

        return list
            .GroupBy(x => (x.Attachment.FileName, x.Attachment.FileUrl), StringTupleComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private enum AttachmentReadStatus
    {
        Success,
        Failed,
        SchemaMismatch
    }

    private sealed record ParsedAttachment(WorkOrderAttachment Attachment, string? Confidentiality);

    private sealed record AttachmentReadResult(
        AttachmentReadStatus Status,
        int HttpStatus,
        IReadOnlyList<WorkOrderAttachment> Attachments,
        bool HasConfidentialityField);

    private sealed class StringTupleComparer : IEqualityComparer<(string A, string B)>
    {
        public static readonly StringTupleComparer OrdinalIgnoreCase = new(StringComparer.OrdinalIgnoreCase);
        private readonly StringComparer _cmp;
        private StringTupleComparer(StringComparer cmp) => _cmp = cmp;

        public bool Equals((string A, string B) x, (string A, string B) y)
            => _cmp.Equals(x.A, y.A) && _cmp.Equals(x.B, y.B);

        public int GetHashCode((string A, string B) obj)
            => HashCode.Combine(_cmp.GetHashCode(obj.A ?? string.Empty), _cmp.GetHashCode(obj.B ?? string.Empty));
    }
}
