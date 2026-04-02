using System.Threading;
using System.Threading.Tasks;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

/// <summary>
/// Writes document attachment links into FSCM.
/// </summary>
public interface IFscmDocuAttachmentsClient
{
    Task<FscmDocuAttachmentUpsertResult> UpsertAsync(
        RunContext ctx,
        FscmDocuAttachmentUpsertRequest request,
        CancellationToken ct);
}

public sealed record FscmDocuAttachmentUpsertRequest(
    string DataAreaId,
    string FileTypeId,
    string FileName,
    string TableName,
    string PrimaryRefNumber,
    string FileUrl,
    string Restriction,
    string SecondaryRefNumber,
    string Name,
    string Notes,
    int Author);

public sealed record FscmDocuAttachmentUpsertResult(
    bool IsSuccess,
    int HttpStatus,
    string? ResponseBody);
