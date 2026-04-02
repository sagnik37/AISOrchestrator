using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.Validation;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services;

/// <summary>
/// Orchestrates FSCM subproject creation for a single Work Order request.
/// Performs request validation, delegates to FSCM client, and emits AIS logs for observability.
/// </summary>
public sealed class SubProjectProvisioningService
{
    private readonly IFscmSubProjectClient _client;
    private readonly IAisLogger _ais;

    public SubProjectProvisioningService(IFscmSubProjectClient client, IAisLogger ais)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _ais = ais ?? throw new ArgumentNullException(nameof(ais));
    }

    public async Task<SubProjectCreateResult> ProvisionAsync(
        RunContext context,
        SubProjectCreateRequest request,
        CancellationToken ct)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (request is null) throw new ArgumentNullException(nameof(request));

        await _ais.InfoAsync(context.RunId, "SubProject", "Subproject provisioning request received.", new
        {
            context.CorrelationId,
            request.LegalEntity,
            request.ParentProjectId,
            request.ProjectName,
            request.WorkOrderId
        }, ct);
        if (!request.ProjectStatus.HasValue)
        {
            request = request with { ProjectStatus = (int?)FscmSubProjectStatus.Inprocess };
        }
        // Basic validation (fail-fast, deterministic) using centralized validator
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            await _ais.WarnAsync(context.RunId, "SubProject", "Subproject provisioning request failed validation.", new
            {
                context.CorrelationId,
                Errors = errors
            }, ct);

            return new SubProjectCreateResult(
                IsSuccess: false,
                parmSubProjectId: null,
                Message: "Validation failed.",
                Errors: errors);
        }

        try
        {
            await _ais.InfoAsync(context.RunId, "SubProject", "Calling FSCM subproject endpoint.", new
            {
                context.CorrelationId
            }, ct);

            var result = await _client.CreateSubProjectAsync(context, request, ct);

            if (result.IsSuccess)
            {
                await _ais.InfoAsync(context.RunId, "SubProject", "FSCM subproject created successfully.", new
                {
                    context.CorrelationId,
                    result.parmSubProjectId,
                    result.Message
                }, ct);
            }
            else
            {
                await _ais.ErrorAsync(context.RunId, "SubProject", "FSCM subproject creation failed.", null, new
                {
                    context.CorrelationId,
                    result.Message,
                    result.Errors
                }, ct);
            }

            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await _ais.WarnAsync(context.RunId, "SubProject", "Subproject provisioning cancelled.", new
            {
                context.CorrelationId
            }, CancellationToken.None);

            throw;
        }
        catch (Exception ex)
        {
            await _ais.ErrorAsync(context.RunId, "SubProject", "Unhandled error while creating FSCM subproject.", ex, new
            {
                context.CorrelationId,
                request.LegalEntity,
                request.ParentProjectId,
                request.ProjectName,
                request.WorkOrderId
            }, ct);

            return new SubProjectCreateResult(
                IsSuccess: false,
                parmSubProjectId: null,
                Message: "Unhandled error occurred while creating subproject.",
                Errors: new[] { new SubProjectError("UNHANDLED_EXCEPTION", ex.Message) });
        }
    }

    /// <summary>
    /// Validates SubProjectCreateRequest using reusable endpoint validator and maps to SubProjectError.
    /// </summary>
    private static List<SubProjectError> Validate(SubProjectCreateRequest request)
    {
        var vErrors = FscmEndpointRequestValidator.Validate(
            FscmEndpointType.SubProjectCreate,
            request,

            //  Explicit generic args fix CS0411
            FscmEndpointRequestValidator.Required<SubProjectCreateRequest>(
                fieldName: "DataAreaId",
                getValue: r => r.DataAreaId,
                errorCode: "AIS_SUBPROJECTCREATE_MISSING_COMPANY",
                message: "DataAreaId (Legal Entity) is mandatory."),

            //FscmEndpointRequestValidator.Required<SubProjectCreateRequest>(
            //    fieldName: "CustomerReference",
            //    getValue: r => r.CustomerReference,
            //    errorCode: "AIS_SUBPROJECTCREATE_MISSING_CUSTOMER",
            //    message: "CustomerReference is mandatory."),

            FscmEndpointRequestValidator.Required<SubProjectCreateRequest>(
                fieldName: "ParentProjectId",
                getValue: r => r.ParentProjectId,
                errorCode: "AIS_SUBPROJECTCREATE_MISSING_PARENT_PROJECT",
                message: "ParentProjectId is mandatory."),

            FscmEndpointRequestValidator.Custom<SubProjectCreateRequest>(
                predicate: r => !string.IsNullOrWhiteSpace(r.WorkOrderId) || !string.IsNullOrWhiteSpace(r.ProjectName),
                fieldName: "WorkOrderId",
                errorCode: "AIS_SUBPROJECTCREATE_MISSING_WORKORDERID",
                message: "WorkOrderId (or legacy ProjectName) is mandatory.")
        );

        return vErrors.Select(e => new SubProjectError(e.Code, e.Message)).ToList();
    }
}
