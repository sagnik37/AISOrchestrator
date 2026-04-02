using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.InvoiceAttributes;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.Services;

/// <summary>
/// Orchestration-level runner that reads InvoiceAttributes from the posting payload
/// and calls the FSCM UpdateInvoiceAttributes endpoint.
///
/// :
/// - This must be called explicitly by orchestrators/endpoints.
/// - Posting (journal validate/create/post) must NOT call invoice update implicitly.
/// </summary>
public sealed class InvoiceAttributesUpdateRunner
{
    private readonly IFscmInvoiceAttributesClient _fscm;
    private readonly ILogger<InvoiceAttributesUpdateRunner> _log;

    public InvoiceAttributesUpdateRunner(IFscmInvoiceAttributesClient fscm, ILogger<InvoiceAttributesUpdateRunner> log)
    {
        _fscm = fscm ?? throw new ArgumentNullException(nameof(fscm));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public sealed record UpdateSummary(int WorkOrdersConsidered, int WorkOrdersWithUpdates, int UpdatePairs, int SuccessCount, int FailureCount);

    public async Task<UpdateSummary> UpdateFromPostingPayloadAsync(RunContext ctx, string postingPayloadJson, CancellationToken ct)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));

        if (string.IsNullOrWhiteSpace(postingPayloadJson))
            return new UpdateSummary(0, 0, 0, 0, 0);

        if (!TryReadWorkOrders(ctx, postingPayloadJson, out var workOrders) || workOrders.Count == 0)
            return new UpdateSummary(0, 0, 0, 0, 0);

        _log.LogInformation("INVOICEATTR_STAGE_BEGIN RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} Stage={Stage} Outcome={Outcome} WorkOrderCount={WorkOrderCount}",
            ctx.RunId, ctx.CorrelationId, ctx.TriggeredBy, TelemetryConventions.Stages.InvoiceAttributesBegin, TelemetryConventions.Outcomes.Accepted, workOrders.Count);

        var considered = workOrders.Count;
        var withUpdates = 0;
        var pairs = 0;
        var ok = 0;
        var fail = 0;

        foreach (var wo in workOrders)
        {
            ct.ThrowIfCancellationRequested();

            if (wo.Updates is null || wo.Updates.Count == 0)
                continue;

            withUpdates++;
            pairs += wo.Updates.Count;

            var res = await _fscm.UpdateAsync(
                ctx,
                company: wo.Company,
                subProjectId: wo.SubProjectId,
                workOrderGuid: wo.WorkOrderGuid,
                workOrderId: wo.WorkOrderId,
                countryRegionId: wo.CountryRegionId,
                county: wo.County,
                state: wo.State,
                dimensionDisplayValue: wo.DimensionDisplayValue,
                fsaTaxabilityType: wo.FSATaxabilityType,
                fsaWellAge: wo.FSAWellAge,
                fsaWorkType: wo.FSAWorkType,
                additionalHeaderFields: wo.AdditionalHeaderFields,
                updates: wo.Updates,
                ct).ConfigureAwait(false);

            if (res.IsSuccess)
            {
                ok++;
                _log.LogInformation(
                    "InvoiceAttributes.Update OK Company={Company} SubProjectId={SubProjectId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid} Pairs={Pairs} HttpStatus={HttpStatus} Stage={Stage} Outcome={Outcome}",
                    wo.Company, wo.SubProjectId, wo.WorkOrderId, wo.WorkOrderGuid, wo.Updates.Count, res.HttpStatus, TelemetryConventions.Stages.InvoiceAttributesEnd, TelemetryConventions.Outcomes.Success);
            }
            else
            {
                fail++;
                _log.LogWarning(
                    "InvoiceAttributes.Update FAILED Company={Company} SubProjectId={SubProjectId} WorkOrderId={WorkOrderId} WorkOrderGuid={WorkOrderGuid} Pairs={Pairs} HttpStatus={HttpStatus} Body={Body} Stage={Stage} Outcome={Outcome} FailureCategory={FailureCategory}",
                    wo.Company, wo.SubProjectId, wo.WorkOrderId, wo.WorkOrderGuid, wo.Updates.Count, res.HttpStatus, LogText.TrimForLog(res.Body ?? string.Empty), TelemetryConventions.Stages.InvoiceAttributesEnd, TelemetryConventions.Outcomes.Failed, TelemetryConventions.ClassifyFailure(null, (System.Net.HttpStatusCode?)res.HttpStatus));
            }
        }

        _log.LogInformation("INVOICEATTR_STAGE_END RunId={RunId} CorrelationId={CorrelationId} SourceSystem={SourceSystem} Stage={Stage} Outcome={Outcome} WorkOrderCount={WorkOrderCount} SucceededCount={SucceededCount} FailedCount={FailedCount}",
            ctx.RunId, ctx.CorrelationId, ctx.TriggeredBy, TelemetryConventions.Stages.InvoiceAttributesEnd, fail > 0 ? (ok > 0 ? TelemetryConventions.Outcomes.Partial : TelemetryConventions.Outcomes.Failed) : (ok > 0 ? TelemetryConventions.Outcomes.Success : TelemetryConventions.Outcomes.Skipped), considered, ok, fail);

        return new UpdateSummary(considered, withUpdates, pairs, ok, fail);
    }

    private sealed record WorkOrderInvoiceUpdates(
        string Company,
        string SubProjectId,
        Guid WorkOrderGuid,
        string WorkOrderId,
        string? CountryRegionId,
        string? County,
        string? State,
        string? DimensionDisplayValue,
        string? FSATaxabilityType,
        string? FSAWellAge,
        string? FSAWorkType,
        IReadOnlyDictionary<string, object?> AdditionalHeaderFields,
        IReadOnlyList<InvoiceAttributePair> Updates);

    private bool TryReadWorkOrders(RunContext ctx, string json, out List<WorkOrderInvoiceUpdates> workOrders)
    {
        workOrders = new List<WorkOrderInvoiceUpdates>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("_request", out var req) || req.ValueKind != JsonValueKind.Object)
                return false;

            if (!req.TryGetProperty("WOList", out var list) || list.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var wo in list.EnumerateArray())
            {
                if (wo.ValueKind != JsonValueKind.Object)
                    continue;

                var company = ReadString(wo, "Company") ?? ReadString(wo, "company");
                var subProjectId = ReadString(wo, "SubProjectId") ?? ReadString(wo, "subProjectId");
                var woGuidStr = ReadString(wo, "WorkOrderGUID") ?? ReadString(wo, "WorkOrderGuid") ?? ReadString(wo, "workOrderGuid");
                var woId = ReadString(wo, "WorkOrderID") ?? ReadString(wo, "WorkOrderId") ?? ReadString(wo, "workOrderId") ?? ReadString(wo, "WONumber") ?? "";

                if (string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(subProjectId) || string.IsNullOrWhiteSpace(woGuidStr) || string.IsNullOrWhiteSpace(woId))
                    continue;

                if (!Guid.TryParse(woGuidStr.Trim().TrimStart('{').TrimEnd('}'), out var woGuid) || woGuid == Guid.Empty)
                    continue;

                if (!wo.TryGetProperty("InvoiceAttributes", out var attrs) || attrs.ValueKind != JsonValueKind.Array)
                    continue;

                var updates = new List<InvoiceAttributePair>();

                foreach (var a in attrs.EnumerateArray())
                {
                    if (a.ValueKind != JsonValueKind.Object)
                        continue;

                    var name = ReadString(a, "AttributeName") ?? ReadString(a, "name");
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    string? val = null;
                    if (a.TryGetProperty("AttributeValue", out var v))
                    {
                        val = v.ValueKind == JsonValueKind.Null ? null : v.ToString();
                    }
                    else if (a.TryGetProperty("value", out var v2))
                    {
                        val = v2.ValueKind == JsonValueKind.Null ? null : v2.ToString();
                    }

                    updates.Add(new InvoiceAttributePair(name!, val));
                }

                if (updates.Count == 0)
                    continue;

                // These header fields are required by FSCM InvoiceAttributes update envelope.
                // Keep them present in the outbound payload even when blank.
                var countryRegionId = ReadString(wo, "CountryRegionId") ?? ReadString(wo, "CountryRegionID") ?? ReadString(wo, "Country");
                var county = ReadString(wo, "County");
                var state = ReadString(wo, "State");
                var ddv = ReadString(wo, "DimensionDisplayValue");
                var tax = ReadString(wo, "FSATaxabilityType");
                var wellAge = ReadString(wo, "FSAWellAge");
                var workType = ReadString(wo, "FSAWorkType");

                var additionalHeaderFields = BuildAdditionalHeaderFields(
                    wo,
                    countryRegionId,
                    county,
                    state,
                    ddv,
                    tax,
                    wellAge,
                    workType);

                workOrders.Add(new WorkOrderInvoiceUpdates(
                    Company: company.Trim(),
                    SubProjectId: subProjectId.Trim(),
                    WorkOrderGuid: woGuid,
                    WorkOrderId: woId.Trim(),
                    CountryRegionId: countryRegionId,
                    County: county,
                    State: state,
                    DimensionDisplayValue: ddv,
                    FSATaxabilityType: tax,
                    FSAWellAge: wellAge,
                    FSAWorkType: workType,
                    AdditionalHeaderFields: additionalHeaderFields,
                    Updates: updates));
            }

            return workOrders.Count > 0;
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "InvoiceAttributesUpdateRunner: failed to parse posting payload JSON. RunId={RunId} CorrelationId={CorrelationId}. Skipping invoice attribute update for this payload.",
                ctx.RunId,
                ctx.CorrelationId);
            return false;
        }


        static IReadOnlyDictionary<string, object?> BuildAdditionalHeaderFields(
            JsonElement wo,
            string? countryRegionId,
            string? county,
            string? state,
            string? dimensionDisplayValue,
            string? fsaTaxabilityType,
            string? fsaWellAge,
            string? fsaWorkType)
        {
            var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            AddString(map, "ActualStartDate", ReadString(wo, "ActualStartDate"));
            AddString(map, "ActualEndDate", ReadString(wo, "ActualEndDate"));
            AddString(map, "ProjectedStartDate", ReadString(wo, "ProjectedStartDate"));
            AddString(map, "ProjectedEndDate", ReadString(wo, "ProjectedEndDate"));
            AddDecimal(map, "Latitude", ReadDecimal(wo, "Latitude"));
            AddDecimal(map, "Longitude", ReadDecimal(wo, "Longitude"));
            AddString(map, "InvoiceNotesInternal", ReadString(wo, "InvoiceNotesInternal") ?? ReadString(wo, "FSAInvoiceNotesInternal") ?? ReadString(wo, "rpc_invoicenotesinternal"));
            AddString(map, "InvoiceNotesExternal", ReadString(wo, "InvoiceNotesExternal") ?? ReadString(wo, "FSAInvoiceNotesExternal") ?? ReadString(wo, "rpc_invoicenotesexternal"));
            AddString(map, "FSACustomerReference", ReadString(wo, "FSACustomerReference") ?? ReadString(wo, "rpc_ponumber"));
            AddString(map, "FSADeclinedToSign", ReadString(wo, "FSADeclinedToSign") ?? ReadString(wo, "rpc_declinedtosignreason"));
            AddString(map, "CountryRegionId", countryRegionId);
            AddString(map, "County", county);
            AddString(map, "State", state);
            AddString(map, "DimensionDisplayValue", dimensionDisplayValue);
            AddString(map, "FSATaxabilityType", fsaTaxabilityType);
            AddString(map, "FSAWellAge", fsaWellAge);
            AddString(map, "FSAWorkType", fsaWorkType);
            AddString(map, "Department", ReadString(wo, "rpc_departments") ?? ReadString(wo, "Department"));
            AddString(map, "ProductLine", ReadString(wo, "rpc_productlines") ?? ReadString(wo, "ProductLine"));
            AddString(map, "Warehouse", ReadString(wo, "rpc_warehouse") ?? ReadString(wo, "Warehouse"));

            return map;
        }

        static decimal? ReadDecimal(JsonElement obj, string prop)
        {
            if (!obj.TryGetProperty(prop, out var p)) return null;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var d)) return d;
            if (p.ValueKind == JsonValueKind.String && decimal.TryParse(p.GetString(), out var ds)) return ds;
            return null;
        }

        static void AddString(IDictionary<string, object?> map, string key, string? value)
            => map[key] = value ?? string.Empty;

        static void AddDecimal(IDictionary<string, object?> map, string key, decimal? value)
            => map[key] = value ?? 0m;

        static string? ReadString(JsonElement obj, string prop)
        {
            if (!obj.TryGetProperty(prop, out var p)) return null;
            if (p.ValueKind == JsonValueKind.String) return p.GetString();
            if (p.ValueKind == JsonValueKind.Null) return null;
            return p.ToString();
        }
    }
}
