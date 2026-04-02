using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services;

/// <summary>
/// Copies (links) Work Order document attachments from Field Service into FSCM.
/// This is a best-effort step and must not break posting in case of attachment issues.
/// </summary>
public sealed class DocumentAttachmentCopyRunner
{
    private readonly IFsWorkOrderAttachmentClient _fs;
    private readonly IFscmDocuAttachmentsClient _fscm;
    private readonly ILogger<DocumentAttachmentCopyRunner> _log;

    public DocumentAttachmentCopyRunner(
        IFsWorkOrderAttachmentClient fs,
        IFscmDocuAttachmentsClient fscm,
        ILogger<DocumentAttachmentCopyRunner> log)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _fscm = fscm ?? throw new ArgumentNullException(nameof(fscm));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task CopyAsync(RunContext ctx, Guid workOrderGuid, string company, string subProjectId, CancellationToken ct)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        if (workOrderGuid == Guid.Empty) throw new ArgumentException("WorkOrderGuid is required.", nameof(workOrderGuid));
        if (string.IsNullOrWhiteSpace(company)) throw new ArgumentException("Company is required.", nameof(company));
        if (string.IsNullOrWhiteSpace(subProjectId)) throw new ArgumentException("SubProjectId is required.", nameof(subProjectId));

        try
        {
            _log.LogInformation("DOCU_ATTACH_STAGE_BEGIN RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} Stage={Stage} Outcome={Outcome} Company={Company} WorkOrderGuid={WorkOrderGuid} SubProjectId={SubProjectId}",
                ctx.RunId, ctx.CorrelationId, ctx.TriggeredBy, TelemetryConventions.Stages.AttachmentsBegin, TelemetryConventions.Outcomes.Accepted, company, workOrderGuid, subProjectId);

            var attachments = await _fs.GetForWorkOrderAsync(ctx, workOrderGuid, ct).ConfigureAwait(false);
            if (attachments.Count == 0)
            {
                _log.LogInformation(
                    "DOCU_ATTACH_STAGE_END RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} Stage={Stage} Outcome={Outcome} Company={Company} WorkOrderGuid={WorkOrderGuid} SubProjectId={SubProjectId} AttachmentCount={AttachmentCount}",
                    ctx.RunId, ctx.CorrelationId, ctx.TriggeredBy, TelemetryConventions.Stages.AttachmentsEnd, TelemetryConventions.Outcomes.Skipped, company, workOrderGuid, subProjectId, 0);
                return;
            }

            _log.LogInformation(
                "DOCU_ATTACH copying {Count} attachment(s). RunId={RunId} CorrelationId={CorrelationId} Company={Company} WorkOrderGuid={WorkOrderGuid} SubProjectId={SubProjectId} Stage={Stage} Outcome={Outcome}",
                attachments.Count, ctx.RunId, ctx.CorrelationId, company, workOrderGuid, subProjectId, TelemetryConventions.Stages.AttachmentsBegin, TelemetryConventions.Outcomes.Accepted);

            foreach (var a in attachments)
            {
                var fileName = ExtractFileNameFromUrl(a.FileName);
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);

                var req = new FscmDocuAttachmentUpsertRequest(
                    DataAreaId: company,
                    FileTypeId: "URL",
                    FileName: fileName,
                    TableName: "Projects",
                    PrimaryRefNumber: subProjectId,
                    FileUrl: a.FileUrl,
                    Restriction: MapRestriction(a),
                    SecondaryRefNumber: string.Empty,
                    Name: nameWithoutExt,
                    Notes: a.FileUrl,
                    Author: 0);

                var resp = await _fscm.UpsertAsync(ctx, req, ct).ConfigureAwait(false);
                if (!resp.IsSuccess)
                {
                    _log.LogError(
                        "DOCU_ATTACH upsert FAILED. HttpStatus={HttpStatus} RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} Stage={Stage} Outcome={Outcome} FailureCategory={FailureCategory} Company={Company} WorkOrderGuid={WorkOrderGuid} SubProjectId={SubProjectId} FileName={FileName}",
                        resp.HttpStatus, ctx.RunId, ctx.CorrelationId, ctx.TriggeredBy, TelemetryConventions.Stages.AttachmentsEnd, TelemetryConventions.Outcomes.Failed, TelemetryConventions.ClassifyFailure(null, (System.Net.HttpStatusCode?)resp.HttpStatus), company, workOrderGuid, subProjectId, a.FileName);
                }
            }

            _log.LogInformation("DOCU_ATTACH_STAGE_END RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} Stage={Stage} Outcome={Outcome} Company={Company} WorkOrderGuid={WorkOrderGuid} SubProjectId={SubProjectId} AttachmentCount={AttachmentCount}",
                ctx.RunId, ctx.CorrelationId, ctx.TriggeredBy, TelemetryConventions.Stages.AttachmentsEnd, TelemetryConventions.Outcomes.Success, company, workOrderGuid, subProjectId, attachments.Count);
        }
        catch (Exception ex)
        {
            _log.LogError(
                ex,
                "DOCU_ATTACH step FAILED (best-effort). RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} Stage={Stage} Outcome={Outcome} FailureCategory={FailureCategory} ErrorType={ErrorType} IsRetryable={IsRetryable} Company={Company} WorkOrderGuid={WorkOrderGuid} SubProjectId={SubProjectId}",
                ctx.RunId, ctx.CorrelationId, ctx.TriggeredBy, TelemetryConventions.Stages.AttachmentsEnd, TelemetryConventions.Outcomes.Failed, TelemetryConventions.ClassifyFailure(ex), ex.GetType().Name, false, company, workOrderGuid, subProjectId);
        }
    }

    private static string MapRestriction(WorkOrderAttachment attachment)
    {
        if (attachment is null) throw new ArgumentNullException(nameof(attachment));

        return string.IsNullOrWhiteSpace(attachment.Confidentiality)
            ? string.Empty
            : attachment.Confidentiality.Trim();
    }

    private static string ExtractFileNameFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        try
        {
            var uri = new Uri(url);
            return Path.GetFileName(uri.LocalPath);
        }
        catch
        {
            // fallback if string is not a valid URI
            return Path.GetFileName(url);
        }
    }
}
