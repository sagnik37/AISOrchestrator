using System;
using System.Collections.Generic;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

/// <summary>
/// Unified options for FSCM side of AIS.
/// Combines previously separate options:
/// - FscmAuthOptions
/// - EndpointsOptions (FSCM section)
/// - FscmBaselineOptions
/// - FscmAccountingPeriodOptions (nested as Periods)
/// </summary>
public sealed class FscmOptions
{
    public const string SectionName = "Fscm";

    // -------------------------
    // Base URLs + Paths
    // -------------------------
    public string BaseUrl { get; set; } = string.Empty;

    public string PostingPath { get; set; } = string.Empty;
    public string? PostingBaseUrlOverride { get; set; }

    // -------------------------
    // Journal pipeline endpoints (placeholders – separate endpoints per step)
    // -------------------------
    public string JournalValidatePath { get; set; } = string.Empty;
    public string JournalCreatePath { get; set; } = string.Empty;
    public string JournalPostCustomPath { get; set; } = string.Empty;
    public string UpdateInvoiceAttributesPath { get; set; } = string.Empty;
    public string UpdateProjectStatusPath { get; set; } = string.Empty;

    /// <summary>
    /// FSCM OData entity set for document attachment links (custom).
    /// Example: /data/RPCCustomDocuAttachments
    /// </summary>
    public string DocuAttachmentsPath { get; set; } = "/data/RPCCustomDocuAttachments";
    /// <summary>
    /// NEW posting endpoint (postJournal) that posts a list of created journals.
    /// </summary>
    public string JournalPostPath { get; set; } = string.Empty;
    // -------------------------
    // Invoice Attributes (runtime mapping + compare)
    // -------------------------
    /// <summary>
    /// Custom endpoint to fetch invoice attribute definitions ("attribute table") for mapping generation.
    /// </summary>
    public string InvoiceAttributeDefinitionsPath { get; set; } = string.Empty;

    /// <summary>
    /// Custom endpoint to fetch current invoice attribute values for a subproject.
    /// </summary>
    public string InvoiceAttributeValuesPath { get; set; } = string.Empty;

    // -------------------------
    // FSCM OData – Released distinct products (Project Category IDs)
    // -------------------------
    public string ReleasedDistinctProductsEntitySet { get; set; } = "CDSReleasedDistinctProducts";
    public int ReleasedDistinctProductsOrFilterChunkSize { get; set; } = 25;

    // -------------------------
    // FSCM OData – Journal history fetch OR-filter batching
    // -------------------------
    /// <summary>
    /// Maximum number of WorkOrder GUIDs to include in a single OData $filter OR expression
    /// when fetching journal history (Item/Expense/Hour) from FSCM.
    ///
    /// This guards against URL-length limits and server-side query parsing constraints.
    /// Default aligns with the FSA chunk default (25), but is independently configurable.
    /// </summary>
    public int JournalHistoryOrFilterChunkSize { get; set; } = 25;


    // Custom FSCM validation endpoint for WO payloads (AIS calls after local validation)
    public string WoPayloadValidationPath { get; set; } = string.Empty;
    public string? WoPayloadValidationBaseUrlOverride { get; set; }

    public string SubProjectPath { get; set; } = string.Empty;
    public string? SubProjectBaseUrlOverride { get; set; }

    public string SingleWorkOrderPath { get; set; } = string.Empty;
    public string? SingleWorkOrderBaseUrlOverride { get; set; }

    public string WorkOrderStatusUpdatePath { get; set; } = string.Empty;
    public string? WorkOrderStatusUpdateBaseUrlOverride { get; set; }

    /// <summary>
    /// Executes resolve base url.
    /// </summary>
    public string ResolveBaseUrl(string? specificOverride)
        => !string.IsNullOrWhiteSpace(specificOverride) ? specificOverride! : BaseUrl;

    // -------------------------
    // Auth (AAD client credentials)
    // -------------------------
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Optional default scope, e.g. https://{host}/.default.
    /// If empty, handler derives scope from request host: https://{requestHost}/.default
    /// </summary>
    public string DefaultScope { get; set; } = string.Empty;

    /// <summary>
    /// Optional per-host overrides. Key must be host only (no scheme).
    /// </summary>
    public Dictionary<string, string> ScopesByHost { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    // -------------------------
    // Baseline scaffold (optional)
    // -------------------------
    public bool BaselineEnabled { get; set; } = false;
    public string BaselineODataBaseUrl { get; set; } = "";
    public string BaselineInProgressFilter { get; set; } = "";
    public string[] BaselineEntitySets { get; set; } = Array.Empty<string>();

    // -------------------------
    // Accounting Periods (delta/reversal)
    // -------------------------
    public FscmAccountingPeriodOptions Periods { get; set; } = new();

    // -------------------------
    // FSCM OData – AttributeTypeGlobalAttributes (Invoice Attribute mapping)
    // -------------------------
    public string AttributeTypeGlobalAttributesEntitySet { get; set; } = "AttributeTypeGlobalAttributes";

}

/// <summary>
/// Config for FSCM accounting period resolution used by delta/reversal logic.
///
/// This is intentionally configurable because FO entity set + field names can vary.
///
/// Semantics intentionally mirror the legacy Dataverse plugin (JobLineEdgeCaseHandler):
/// - If no period is found for a date => treat as OPEN
/// - If no ledger status row is found for a period/ledger => treat as OPEN
/// - A period is considered OPEN only if PeriodStatus matches OpenPeriodStatusValue(s).
/// </summary>
public sealed class FscmAccountingPeriodOptions
{
    // -------------------- Fiscal calendar lookup --------------------
    public string FiscalCalendarEntitySet { get; set; } = "FiscalCalendarsEntity";
    public string FiscalCalendarNameField { get; set; } = "Description";
    public string FiscalCalendarIdField { get; set; } = "CalendarId";
    public string? FiscalCalendarName { get; set; } = "Fiscal Calendar";

    // New requirement: calendar id is provided directly (e.g. "Fis Cal"). If set, AIS will use this value first.
    public string? FiscalCalendarIdOverride { get; set; } = "Fis Cal";

    // -------------------- Fiscal period lookup --------------------
    public string FiscalPeriodEntitySet { get; set; } = "FiscalPeriods";
    public string FiscalPeriodYearField { get; set; } = "FiscalYear";
    public string FiscalPeriodNameField { get; set; } = "PeriodName";
    public string FiscalPeriodStartDateField { get; set; } = "StartDate";
    public string FiscalPeriodEndDateField { get; set; } = "EndDate";
    public string FiscalPeriodCalendarField { get; set; } = "Calendar";

    // -------------------- Ledger period status lookup --------------------
    public string LedgerFiscalPeriodEntitySet { get; set; } = "LedgerFiscalPeriodsV2";
    public string LedgerFiscalPeriodLedgerField { get; set; } = "LedgerName";
    public string LedgerFiscalPeriodCalendarField { get; set; } = "Calendar";
    public string LedgerFiscalPeriodYearField { get; set; } = "YearName";
    public string LedgerFiscalPeriodPeriodField { get; set; } = "PeriodName";
    public string LedgerFiscalPeriodLegalEntityField { get; set; } = "LegalEntityId";
    public string LedgerFiscalPeriodStatusField { get; set; } = "PeriodStatus";

    public string Ledger { get; set; } = "001";
    public string OpenPeriodStatusValue { get; set; } = "Open";
    public List<string>? OpenPeriodStatusValues { get; set; }

    // -------------------- Optional company filter --------------------
    public string? DataAreaField { get; set; } = "DataAreaId";
    public string? DataAreaId { get; set; }

    // -------------------- Closed reversal policy --------------------

    /// <summary>
    /// Feature toggle for accounting period logic.
    ///
    /// When true (default), AIS will classify FSCM journal history into CLOSED vs OPEN periods and apply
    /// the configured closed reversal strategy.
    ///
    /// When false, AIS will NOT call FSCM period APIs for classification; all history is treated as OPEN and
    /// ALL reversal/positive delta lines will use the job run date as TransactionDate.
    /// </summary>
    public bool EnableAccountingPeriodChecks { get; set; } = true;

    /// <summary>
    /// If true, AIS derives the job run date (TransactionDate) using <see cref="LocalTimeZoneId"/>
    /// instead of UTC. Only the DATE component is used in payloads.
    /// </summary>
    public bool UseLocalTimeZoneForTransactionDate { get; set; } = false;

    /// <summary>
    /// IANA time zone id used when <see cref="UseLocalTimeZoneForTransactionDate"/> is true.
    /// Example: "Asia/Kolkata".
    /// </summary>
    public string LocalTimeZoneId { get; set; } = "Asia/Kolkata";

    public string ClosedReversalDateStrategy { get; set; } = "CurrentOpenPeriodStart";
    public int OutOfWindowDateCacheSize { get; set; } = 512;

    // -------------------- Window sizing --------------------
    public int LookbackDays { get; set; } = 120;
    public int LookaheadDays { get; set; } = 120;
    public int LedgerStatusOrFilterChunkSize { get; set; } = 40;

    // -------------------- Backward-compat --------------------
    public int OpenStatusCode { get; set; } = 743860002;
    public string FiscalPeriodIdField { get; set; } = "FiscalPeriodId";
    public string LedgerFiscalPeriodPeriodField_Legacy { get; set; } = "FiscalPeriodId";
}
