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


public sealed class FsaODataQueryBuilder : IFsaODataQueryBuilder
{
    /// <summary>
    /// Executes build open work orders relative.
    /// </summary>
    public string BuildOpenWorkOrdersRelative(string filter, int pageSize)
    {
        if (string.IsNullOrWhiteSpace(filter)) throw new ArgumentException(nameof(filter));
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));

        // Work order header fields required across all AIS lifecycles.
        // Includes invoice attributes (rpc_*) for attribute sync to FSCM.
        var select =
            "msdyn_workorderid,msdyn_name,_msdyn_serviceaccount_value,modifiedon," +
            DataverseSchema.Lookup_SubProject +
            // Invoice attribute candidates
            ",rpc_wellname,rpc_wellnumber,rpc_area,rpc_representativeid,rpc_welllocale,rpc_manufacturingplant," +
            "rpc_afe_wbsnumber,rpc_leasename,rpc_ocsgnumber,rpc_rig,rpc_pricelist," +
            // Actual/Projected dates (support both custom + OOB)
            "rpc_utcactualstartdate,rpc_utcactualenddate," +
            "msdyn_timefrompromised,msdyn_timetopromised," +
            // Lat/Long + notes
            "rpc_welllatitude,rpc_welllongitude,rpc_invoicenotesinternal,rpc_ponumber,rpc_declinedtosignreason," +
            // Header dimension lookups
            "_rpc_departments_value,_rpc_productlines_value,_rpc_warehouse_value," +
            // Needed for worktype lookup enrichment + taxability enrichment + location header fields
            "_rpc_worktypelookup_value,_rpc_operationtype_value," +
            "_rpc_countrylookup_value,_rpc_countylookup_value,_rpc_statelookup_value";
        var expand =
            $"{DataverseSchema.Nav_ServiceAccount}($select={DataverseSchema.AccountCompanyLookupField})";

        return
            $"msdyn_workorders" +
            $"?$select={select}" +
            $"&$expand={expand}" +
            $"&$filter={filter}" +
            $"&$orderby=modifiedon asc" +
            $"&$top={pageSize}";
    }

    /// <summary>
    /// Executes build work orders relative.
    /// </summary>
    public string BuildWorkOrdersRelative(IReadOnlyCollection<Guid> ids, int pageSize)
    {
        var select =
            "msdyn_workorderid,msdyn_name,modifiedon,_msdyn_serviceaccount_value," +
            DataverseSchema.Lookup_SubProject +
            // Invoice attribute candidates
            ",rpc_wellname,rpc_wellnumber,rpc_area,rpc_representativeid,rpc_welllocale,rpc_manufacturingplant," +
            "rpc_afe_wbsnumber,rpc_leasename,rpc_ocsgnumber,rpc_rig,rpc_pricelist," +
            // Actual/Projected dates (support both custom + OOB)
            "rpc_utcactualstartdate,rpc_utcactualenddate,msdyn_actualstarttime,msdyn_actualendtime," +
            "msdyn_timefrompromised,msdyn_timetopromised," +
            // Lat/Long + notes
            "rpc_welllatitude,rpc_welllongitude,rpc_invoicenotesinternal,rpc_ponumber,rpc_declinedtosignreason," +
            // Header dimension lookups
            "_rpc_departments_value,_rpc_productlines_value,_rpc_warehouse_value," +
            // Needed for worktype lookup enrichment + taxability enrichment + location header fields
            "_rpc_worktypelookup_value,_rpc_operationtype_value," +
            "_rpc_countrylookup_value,_rpc_countylookup_value,_rpc_statelookup_value";
        var expand =
            $"{DataverseSchema.Nav_ServiceAccount}($select={DataverseSchema.AccountCompanyLookupField})";

        return BuildEntityByIdsRelative("msdyn_workorders", "msdyn_workorderid", ids, select, expand, "modifiedon asc", pageSize);
    }

    /// <summary>
    /// Executes build work order products relative.
    /// </summary>
    public string BuildWorkOrderProductsRelative(IReadOnlyCollection<Guid> workOrderIds, int pageSize)
    {
        var select = string.Join(",",
            "msdyn_workorderproductid",
            "_msdyn_workorder_value",
            "_msdyn_product_value",
            "msdyn_quantity",
            "rpc_extendedamount",
            DataverseSchema.Lookup_ProductLine,
            DataverseSchema.Lookup_Department,
            DataverseSchema.WorkOrderProductWarehouseLookupField,
            "rpc_calculatedunitprice",
            DataverseSchema.Lookup_LineProperty,
            "statecode",
            //"_msdyn_worker_value",
            "msdyn_name",
            "msdyn_estimateunitcost",
            "msdyn_unitcost",
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
            "rpc_operationsdate",
            "modifiedon",
            "transactioncurrencyid",
            "msdyn_lineorder"
        );

        var expand =
            $"{DataverseSchema.Nav_Currency}($select={DataverseSchema.CurrencyCodeField}),";

        return BuildEntityByIdsRelative("msdyn_workorderproducts", "_msdyn_workorder_value", workOrderIds, select, expand, "modifiedon asc", pageSize);
    }

    /// <summary>
    /// Executes build work order services relative.
    /// </summary>
    public string BuildWorkOrderServicesRelative(IReadOnlyCollection<Guid> workOrderIds, int pageSize)
    {
        var select = string.Join(",",
            "msdyn_workorderserviceid",
            "_msdyn_workorder_value",
            "_msdyn_service_value",
            "msdyn_quantity",
            "rpc_extendedamount",
            DataverseSchema.Lookup_ProductLine,
            DataverseSchema.Lookup_Department,
            "rpc_calculatedunitprice",
            DataverseSchema.Lookup_LineProperty,
            "statecode",
            "_rpc_worker_value",
            "msdyn_name",
            "msdyn_estimateunitcost",
            "msdyn_unitcost",
            "msdyn_description",
            "rpc_lineitemdiscountamount",
            "rpc_lineitemdiscount",
            "rpc_surchargeamount",
            "rpc_surchage",
            "rpc_applyoveralldiscountamount",
            "rpc_applyoveralldiscount",
            "rpc_customerserviceid",
            "rpc_lineitemmarkup",
            "rpc_lineitemmarkupamount",
            "msdyn_totalamount",
            "_msdyn_unit_value",
            "rpc_operationsdate",
            "modifiedon",
            "transactioncurrencyid",
            "msdyn_lineorder"
        );

        var expand =
            $"{DataverseSchema.Nav_Currency}($select={DataverseSchema.CurrencyCodeField}),";

        return BuildEntityByIdsRelative("msdyn_workorderservices", "_msdyn_workorder_value", workOrderIds, select, expand, "modifiedon asc", pageSize);
    }

    /// <summary>
    /// Executes build products relative.
    /// </summary>
    public string BuildProductsRelative(IReadOnlyCollection<Guid> productIds, int pageSize)
    {
        var select = string.Join(",",
            "productid",
            "msdyn_productnumber",
            "msdyn_fieldserviceproducttype",
            "msdyn_productcolor",
            "msdyn_productconfiguration",
            "msdyn_productsize",
            "msdyn_productstyle"
        );

        return BuildEntityByIdsRelative("products", "productid", productIds, select, expand: null, orderBy: null, pageSize);
    }

    /// <summary>
    /// Executes build work order product presence relative.
    /// </summary>
    public string BuildWorkOrderProductPresenceRelative(IReadOnlyCollection<Guid> workOrderIds)
        => BuildPresenceRelative("msdyn_workorderproducts", "_msdyn_workorder_value", workOrderIds);

    /// <summary>
    /// Executes build work order service presence relative.
    /// </summary>
    public string BuildWorkOrderServicePresenceRelative(IReadOnlyCollection<Guid> workOrderIds)
        => BuildPresenceRelative("msdyn_workorderservices", "_msdyn_workorder_value", workOrderIds);

    /// <summary>
    /// Executes build warehouses by ids relative.
    /// </summary>
    public string BuildWarehousesByIdsRelative(IReadOnlyCollection<Guid> warehouseIds)
    {
        if (warehouseIds is null) throw new ArgumentNullException(nameof(warehouseIds));
        if (warehouseIds.Count == 0) return $"{DataverseSchema.WarehouseEntitySet}?$select={DataverseSchema.WarehouseIdAttribute}&$top=1&$filter=false";

        var select = string.Join(",",
            DataverseSchema.WarehouseIdAttribute,
            DataverseSchema.WarehouseIdentifierField,
            DataverseSchema.WarehouseOperationalSiteLookupField
        );

        var filter = BuildOrGuidFilter(DataverseSchema.WarehouseIdAttribute, warehouseIds);

        return $"{DataverseSchema.WarehouseEntitySet}?$select={select}&$filter={filter}";
    }
    /// <summary>
    /// Executes build operational sites by ids relative.
    /// </summary>
    public string BuildOperationalSitesByIdsRelative(IReadOnlyCollection<Guid> operationalSiteIds)
    {
        if (operationalSiteIds is null) throw new ArgumentNullException(nameof(operationalSiteIds));
        if (operationalSiteIds.Count == 0) throw new ArgumentException("Must contain at least one id", nameof(operationalSiteIds));

        var select = string.Join(",",
            DataverseSchema.OperationalSiteIdAttribute,
            DataverseSchema.OperationalSiteSiteIdField
        );

        var filter = BuildOrGuidFilter(DataverseSchema.OperationalSiteIdAttribute, operationalSiteIds);

        return $"{DataverseSchema.OperationalSiteEntitySet}?$select={select}&$filter={filter}";
    }


    /// <summary>
    /// Executes build virtual lookup by ids relative.
    /// </summary>
    public string BuildVirtualLookupByIdsRelative(string entitySetName, string idAttribute, IReadOnlyCollection<Guid> ids)
    {
        if (string.IsNullOrWhiteSpace(entitySetName)) throw new ArgumentException(nameof(entitySetName));
        if (string.IsNullOrWhiteSpace(idAttribute)) throw new ArgumentException(nameof(idAttribute));
        if (ids is null) throw new ArgumentNullException(nameof(ids));
        if (ids.Count == 0) return $"{entitySetName}?$select={idAttribute}&$top=1&$filter=false";

        var filter = BuildOrGuidFilter(idAttribute, ids);
        return $"{entitySetName}?$select={idAttribute},mserp_name&$filter={filter}";
    }

    private static string BuildEntityByIdsRelative(
        string entitySetName,
        string idProperty,
        IReadOnlyCollection<Guid> ids,
        string select,
        string? expand,
        string? orderBy,
        int top)
    {
        if (string.IsNullOrWhiteSpace(entitySetName)) throw new ArgumentException(nameof(entitySetName));
        if (string.IsNullOrWhiteSpace(idProperty)) throw new ArgumentException(nameof(idProperty));
        if (ids is null) throw new ArgumentNullException(nameof(ids));
        if (ids.Count == 0) return $"{entitySetName}?$select={select}&$top=1&$filter=false";
        if (top <= 0) throw new ArgumentOutOfRangeException(nameof(top));

        var filter = BuildOrGuidFilter(idProperty, ids);

        var url =
            $"{entitySetName}" +
            $"?$select={select}" +
            (string.IsNullOrWhiteSpace(expand) ? "" : $"&$expand={expand}") +
            $"&$filter={filter}" +
            (string.IsNullOrWhiteSpace(orderBy) ? "" : $"&$orderby={orderBy}") +
            $"&$top={top}";

        return url;
    }

    /// <summary>
    /// Executes build presence relative.
    /// </summary>
    private static string BuildPresenceRelative(string entitySetName, string idProperty, IReadOnlyCollection<Guid> ids)
    {
        if (ids is null) throw new ArgumentNullException(nameof(ids));
        if (ids.Count == 0) return $"msdyn_workorders?$select=msdyn_workorderid&$top=1&$filter=false";

        var filter = BuildOrGuidFilter(idProperty, ids);
        return $"{entitySetName}?$select={idProperty}&$filter={filter}&$top=1";
    }

    /// <summary>
    /// Executes build or guid filter.
    /// </summary>
    private static string BuildOrGuidFilter(string field, IEnumerable<Guid> ids)
    {
        var parts = ids.Where(g => g != Guid.Empty)
                       .Distinct()
                       .Select(g => $"{field} eq {ToGuidLiteral(g)}")
                       .ToArray();

        if (parts.Length == 0) return "false";
        if (parts.Length == 1) return parts[0];
        return "(" + string.Join(" or ", parts) + ")";
    }

    private static string ToGuidLiteral(Guid id) => id.ToString("D");
}
