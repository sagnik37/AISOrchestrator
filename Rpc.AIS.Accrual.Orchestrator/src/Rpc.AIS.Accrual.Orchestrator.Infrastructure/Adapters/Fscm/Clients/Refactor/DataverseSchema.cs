// File: .../Infrastructure/Clients/Refactor/DataverseSchema.cs
// Centralized Dataverse schema constants used by FSA ingestion clients.

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Provides dataverse schema behavior.
/// </summary>
internal static class DataverseSchema
{
    internal const string Nav_ServiceAccount = "msdyn_serviceaccount";
    internal const string Nav_Currency = "transactioncurrencyid";
    internal const string Nav_Worker = "msdyn_worker";

    internal const string Lookup_SubProject = "_rpc_subproject_value";

    internal const string FlatSubProjectField = "SubProjectId";
    internal const string FlatSubProjectNameField = "SubProjectName";
internal const string CurrencyCodeField = "isocurrencycode";
    internal const string WorkerNumberField = "cdm_workernumber";

    // Warehouse
    internal const string WorkOrderProductWarehouseLookupField = "_msdyn_warehouse_value";
    internal const string WarehouseEntitySet = "msdyn_warehouses";
    internal const string WarehouseIdAttribute = "msdyn_warehouseid";
    internal const string WarehouseIdentifierField = "msdyn_warehouseidentifier";
    internal const string WarehouseOperationalSiteLookupField = "_msdyn_operationalsite_value";

    // Operational Site
    internal const string OperationalSiteEntitySet = "msdyn_operationalsites";
    internal const string OperationalSiteIdAttribute = "msdyn_operationalsiteid";
    internal const string OperationalSiteSiteIdField = "msdyn_siteid";

    // OData
    internal const string ODataFormattedSuffix = "@OData.Community.Display.V1.FormattedValue";

    // Payload aliases
    internal const string PayloadWarehouseField = "Warehouse";
    internal const string PayloadSiteField = "Site";
    internal const string FlatSiteIdField = "msdyn_siteid";

    // Company / project
    internal const string AccountCompanyLookupField = "_msdyn_company_value";
    internal const string FlatCompanyCodeField = "cdm_companycode";
    internal const string FlatCompanyIdField = "cdm_companyid";

    //UPDATED LOOKUPS
    // Line Property is now an Option Set (NOT a lookup). We read the label via FormattedValue.
    internal const string Lookup_LineProperty = "rpc_lineproperties";

    internal const string Lookup_Department = "_rpc_departments_value";
    internal const string Lookup_ProductLine = "_rpc_productlines_value";

    // Flat output fields (unchanged, backward compatible)
    internal const string FlatLinePropertyNameField = "LinePropertyName";
    internal const string FlatDepartmentNameField = "DepartmentName";
    internal const string FlatProductLineNameField = "ProductLineName";
}
