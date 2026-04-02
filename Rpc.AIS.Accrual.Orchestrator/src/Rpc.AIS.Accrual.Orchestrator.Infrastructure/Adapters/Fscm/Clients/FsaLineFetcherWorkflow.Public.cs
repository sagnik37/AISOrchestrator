// File: FsaLineFetcherWorkflow.Public.cs


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



    public async Task<JsonDocument> GetOpenWorkOrdersAsync(
        string filter,
        int pageSize,
        int maxPages,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(filter))
            throw new ArgumentException("Open work order filter must be provided.", nameof(filter));
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (maxPages <= 0) throw new ArgumentOutOfRangeException(nameof(maxPages));

        var relative = _qb.BuildOpenWorkOrdersRelative(filter, pageSize);

        _log.LogInformation("Dataverse OPEN WO query (relative): {RelativeUrl}", relative);

        var woHeaders = await _reader.ReadAllPagesAsync(
            http: _http,
            initialRelativeUrl: relative,
            maxPages: maxPages,
            logEntityName: "OpenWorkOrders",
            ct: ct).ConfigureAwait(false);

        var flattened = _flattener.FlattenWorkOrderCompanyFromExpand(woHeaders);
        var processed = PostProcessWorkOrders(flattened, _opt.RequireSubProjectForProcessing);
        processed = await EnrichWorkOrdersWithWorkTypeFieldsAsync(processed, ct).ConfigureAwait(false);
        processed = await EnrichWithTaxabilityTypeAsync(processed, ct).ConfigureAwait(false);
        return processed;
    }


    private static JsonDocument PostProcessWorkOrders(JsonDocument doc, bool requireSubProject)
    {
        // Behavior-preserving augmentation:
        // - Adds SubProjectId/SubProjectName if present (supports both rpc_subproject and legacy msdyn_fnosubproject lookups)
        // - Optionally filters out work orders missing subproject when requireSubProject=true (default false)
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            var root = doc.RootElement;
            writer.WriteStartObject();

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.NameEquals("value"))
                {
                    writer.WritePropertyName("value");
                    writer.WriteStartArray();

                    foreach (var row in prop.Value.EnumerateArray())
                    {
                        var hasSub = TryGetLookup(row, DataverseSchema.Lookup_SubProject, out var subId, out var subName);

                        if (requireSubProject && !hasSub)
                        {
                            continue;
                        }

                        writer.WriteStartObject();
                        foreach (var c in row.EnumerateObject())
                        {
                            c.WriteTo(writer);
                        }

                        if (hasSub)
                        {
                            writer.WriteString(DataverseSchema.FlatSubProjectField, subId);
                            if (!string.IsNullOrWhiteSpace(subName))
                            {
                                writer.WriteString(DataverseSchema.FlatSubProjectNameField, subName);
                            }
                        }

                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                }
                else
                {
                    prop.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        stream.Position = 0;
        return JsonDocument.Parse(stream);
    }

    private static bool TryGetLookup(JsonElement row, string lookupField, out string id, out string name)
    {
        id = "";
        name = "";

        if (row.ValueKind != JsonValueKind.Object) return false;

        if (row.TryGetProperty(lookupField, out var v) && v.ValueKind == JsonValueKind.String)
        {
            id = v.GetString() ?? "";
        }

        // formatted value for the lookup
        var formattedKey = lookupField + DataverseSchema.ODataFormattedSuffix;
        if (row.TryGetProperty(formattedKey, out var fv) && fv.ValueKind == JsonValueKind.String)
        {
            name = fv.GetString() ?? "";
        }

        return !string.IsNullOrWhiteSpace(id) || !string.IsNullOrWhiteSpace(name);
    }

    // =====================================================================
    //  Remaining methods preserve behavior but now delegate paging/enrichment
    // to SRP components.
    // =====================================================================

    public async Task<JsonDocument> GetWorkOrdersAsync(
        IReadOnlyList<Guid> woIds,
        CancellationToken ct)
    {
        if (woIds is null) throw new ArgumentNullException(nameof(woIds));
        if (woIds.Count == 0) return EmptyValueDocument();

        var select = string.Join(",",
            "msdyn_workorderid",
            "msdyn_name",
            "modifiedon",
            "_msdyn_serviceaccount_value",
            DataverseSchema.Lookup_SubProject,

            // Invoice attribute candidates on msdyn_workorder
            "rpc_wellnametext",
            "rpc_wellnumber",
            "rpc_area",
            "rpc_representativeid",
            "rpc_welllocale",
            "rpc_manufacturingplant",
            "rpc_afe_wbsnumber",
            "rpc_customersignature",
            "rpc_leasename",
            "rpc_ocsgnumber",
            "rpc_rig",
            "rpc_pricelist",
            "rpc_welllatitude",
            "rpc_welllongitude",
            "rpc_invoicenotesinternal",
            "rpc_declinedtosignreason",
            "rpc_invoicenotesexternal",
            "_rpc_productlines_value",
            "_rpc_warehouse_value",
            "_rpc_departments_value",
            "rpc_ponumber",
            "rpc_utcactualstartdate",
            "rpc_utcactualenddate",
            // Lookups (we read *_value and formatted value)
            "_rpc_countrylookup_value",
            "_rpc_countylookup_value",
            "_rpc_statelookup_value",
            "_rpc_worktypelookup_value",
            "_rpc_operationtype_value"
        );

        var expand =
            $"{DataverseSchema.Nav_ServiceAccount}($select={DataverseSchema.AccountCompanyLookupField})";

        var woHeaders = await AggregateByIdsAsync(
            entitySetName: "msdyn_workorders",
            idProperty: "msdyn_workorderid",
            ids: woIds,
            select: select,
            expand: expand,
            orderBy: "modifiedon asc",
            chunkSize: (_opt.OrFilterChunkSize > 0 ? _opt.OrFilterChunkSize : DefaultChunkSize),
            ct: ct).ConfigureAwait(false);

        var flattened = _flattener.FlattenWorkOrderCompanyFromExpand(woHeaders);
        var processed = PostProcessWorkOrders(flattened, _opt.RequireSubProjectForProcessing);
        processed = await EnrichWorkOrdersWithWorkTypeFieldsAsync(processed, ct).ConfigureAwait(false);
        processed = await EnrichWithTaxabilityTypeAsync(processed, ct).ConfigureAwait(false);
        return processed;
    }

    public async Task<JsonDocument> GetWorkOrderProductsAsync(
        IReadOnlyList<Guid> woIds,
        CancellationToken ct)
    {
        if (woIds is null) throw new ArgumentNullException(nameof(woIds));
        if (woIds.Count == 0) return EmptyValueDocument();

        var select = string.Join(",",
            "msdyn_workorderproductid",
            "_msdyn_workorder_value",
            "_msdyn_product_value",
            "msdyn_quantity",
            "rpc_extendedamount",
            "_rpc_productlines_value",
            "_rpc_departments_value",
            DataverseSchema.WorkOrderProductWarehouseLookupField,
            "rpc_calculatedunitprice",
            "rpc_lineproperties",
            "statecode",
            //"_msdyn_worker_value",
            "msdyn_name",
            "msdyn_estimateunitcost",
            "msdyn_unitcost",
            "msdyn_unitamount",
            "msdyn_description",
            "rpc_lineitemdiscountamount",
            "rpc_lineitemdiscount",
            "rpc_surchargeamount",
            "rpc_surchage",
            "rpc_applyoveralldiscountamount",
            "rpc_applyoveralldiscount",
            "rpc_customerproductid",
            "rpc_lineitemmarkup",
            "rpc_lineitemmarkupamount",
            "msdyn_totalamount",
            "_msdyn_unit_value",
            //"msdyn_dataareaid",
            "rpc_operationsdate",
            "rpc_printable",
           // Invoice attribute (Option B) helpers
           "_rpc_operationtype_value",
           //"_rpc_taxabilitytype_value",
           "modifiedon",
           "transactioncurrencyid",
           "msdyn_lineorder"
       );

        // Keep the existing expansions that already work in  org.
        // We DO NOT rely on warehouse expand for operational site anymore.
        var expand =
            $"{DataverseSchema.Nav_Currency}($select={DataverseSchema.CurrencyCodeField})";
        //$"{DataverseSchema.Nav_Worker}($select={DataverseSchema.WorkerNumberField})";

        var baseLines = await AggregateByIdsAsync(
            entitySetName: "msdyn_workorderproducts",
            idProperty: "_msdyn_workorder_value",
            ids: woIds,
            select: select,
            expand: expand,
            orderBy: "modifiedon asc",
            chunkSize: (_opt.OrFilterChunkSize > 0 ? _opt.OrFilterChunkSize : DefaultChunkSize),
            ct: ct).ConfigureAwait(false);

        // enrich WOP rows by querying msdyn_warehouses using _msdyn_warehouse_value
        // and extracting:
        //  - msdyn_warehouseidentifier
        //  - _msdyn_operationalsite_value@FormattedValue (site id)
        var withWarehouseAndSite = await _warehouseEnricher.EnrichWopLinesFromWarehousesAsync(_http, baseLines, ct).ConfigureAwait(false);

        // Keep existing virtual lookup enrichment (line property + financial dims)
        if (_opt.DisableVirtualLookupResolution)
        {
            return await EnrichWithTaxabilityTypeAsync(withWarehouseAndSite, ct).ConfigureAwait(false);
        }

        var enriched = await _virtualLookupResolver.EnrichLinesWithVirtualLookupNamesAsync(_http, withWarehouseAndSite, ct).ConfigureAwait(false);
        return await EnrichWithTaxabilityTypeAsync(enriched, ct).ConfigureAwait(false);
    }

    public async Task<JsonDocument> GetWorkOrderServicesAsync(
        IReadOnlyList<Guid> woIds,
        CancellationToken ct)
    {
        if (woIds is null) throw new ArgumentNullException(nameof(woIds));
        if (woIds.Count == 0) return EmptyValueDocument();

        var select = string.Join(",",
            "msdyn_workorderserviceid",
            "_msdyn_workorder_value",
            "_msdyn_service_value",
            "msdyn_duration",
            "rpc_extendedamount",
            "_rpc_productlines_value",
            "_rpc_departments_value",
           // "_rpc_warehouse_value",
            "rpc_calculatedunitprice",
            "rpc_lineproperties",
            "statecode",
            "_rpc_worker_value",
            "msdyn_name",
            "msdyn_estimateunitcost",
            "msdyn_unitcost",
            "rpc_lineitemdiscountamount",
            "rpc_lineitemdiscount",
            "rpc_surchargeamount",
            "rpc_surcharge",
            "rpc_applyoveralldiscountamount",
            "rpc_applyoveralldiscount",
            "rpc_customerproductid",
            "rpc_lineitemmarkup",
            "rpc_lineitemmarkupamount",
            "msdyn_totalamount",
            "msdyn_unit",
            "rpc_operationsdate",
            "rpc_printable",
            "_rpc_operationtype_value",
            "modifiedon",
            "transactioncurrencyid",
            "msdyn_lineorder",
            "msdyn_description"
        );

        var expand =
            $"{DataverseSchema.Nav_Currency}($select={DataverseSchema.CurrencyCodeField})";

        var baseLines = await AggregateByIdsAsync(
            entitySetName: "msdyn_workorderservices",
            idProperty: "_msdyn_workorder_value",
            ids: woIds,
            select: select,
            expand: expand,
            orderBy: "modifiedon asc",
            chunkSize: (_opt.OrFilterChunkSize > 0 ? _opt.OrFilterChunkSize : DefaultChunkSize),
            ct: ct).ConfigureAwait(false);

        // Services do not use warehouse/site per  earlier logic.
        if (_opt.DisableVirtualLookupResolution)
        {
            return await EnrichWithTaxabilityTypeAsync(baseLines, ct).ConfigureAwait(false);
        }

        var enriched = await _virtualLookupResolver.EnrichLinesWithVirtualLookupNamesAsync(_http, baseLines, ct).ConfigureAwait(false);
        return await EnrichWithTaxabilityTypeAsync(enriched, ct).ConfigureAwait(false);
    }
    // --------------------
    // Existing helpers 
    // --------------------

    public async Task<JsonDocument> GetProductsAsync(
        IReadOnlyList<Guid> productIds,
        CancellationToken ct)
    {
        if (productIds is null) throw new ArgumentNullException(nameof(productIds));
        if (productIds.Count == 0) return EmptyValueDocument();

        var select = string.Join(",",
            "productid",
            "msdyn_productnumber",
            "msdyn_fieldserviceproducttype",
            "msdyn_productcolor",
            "msdyn_productconfiguration",
            "msdyn_productsize",
            "msdyn_productstyle"
        );

        var raw = await AggregateByIdsAsync(
            entitySetName: "products",
            idProperty: "productid",
            ids: productIds,
            select: select,
            expand: null,
            orderBy: null,
            chunkSize: (_opt.OrFilterChunkSize > 0 ? _opt.OrFilterChunkSize : DefaultChunkSize),
            ct: ct).ConfigureAwait(false);

        return raw;
    }

    /// <summary>
    /// Executes get work order product presence async.
    /// </summary>
    public async Task<JsonDocument> GetWorkOrderProductPresenceAsync(IReadOnlyList<Guid> woIds, CancellationToken ct)
    {
        if (woIds is null) throw new ArgumentNullException(nameof(woIds));
        if (woIds.Count == 0) return EmptyValueDocument();

        return await AggregateByIdsAsync(
            entitySetName: "msdyn_workorderproducts",
            idProperty: "_msdyn_workorder_value",
            ids: woIds,
            select: "_msdyn_workorder_value",
            expand: null,
            orderBy: null,
            chunkSize: (_opt.OrFilterChunkSize > 0 ? _opt.OrFilterChunkSize : DefaultChunkSize),
            ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes get work order service presence async.
    /// </summary>
    public async Task<JsonDocument> GetWorkOrderServicePresenceAsync(IReadOnlyList<Guid> woIds, CancellationToken ct)
    {
        if (woIds is null) throw new ArgumentNullException(nameof(woIds));
        if (woIds.Count == 0) return EmptyValueDocument();

        return await AggregateByIdsAsync(
            entitySetName: "msdyn_workorderservices",
            idProperty: "_msdyn_workorder_value",
            ids: woIds,
            select: "_msdyn_workorder_value",
            expand: null,
            orderBy: null,
            chunkSize: (_opt.OrFilterChunkSize > 0 ? _opt.OrFilterChunkSize : DefaultChunkSize),
            ct: ct).ConfigureAwait(false);
    }






}
