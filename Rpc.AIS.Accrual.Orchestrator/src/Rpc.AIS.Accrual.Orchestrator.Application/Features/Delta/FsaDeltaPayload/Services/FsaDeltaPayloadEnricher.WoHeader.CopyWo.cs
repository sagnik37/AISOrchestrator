// File: .../FsaDeltaPayloadEnricher.WoHeader.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.Enrichment;

using static Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.FsaDeltaPayloadJsonUtil;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload;

public sealed partial class FsaDeltaPayloadEnricher
{
    private static void CopyWoWithWoHeaderFieldsInjection(
      JsonElement wo,
      Utf8JsonWriter w,
      IReadOnlyDictionary<Guid, WoHeaderMappingFields> woIdToHeaderFields)
    {
        Guid? woId = null;

        if (wo.ValueKind == JsonValueKind.Object)
        {
            if (wo.TryGetProperty("WorkOrderGUID", out var g1) && g1.ValueKind == JsonValueKind.String)
                woId = ParseGuidLoose(g1.GetString());
            else if (wo.TryGetProperty("WorkorderGUID", out var g2) && g2.ValueKind == JsonValueKind.String)
                woId = ParseGuidLoose(g2.GetString());
        }

        WoHeaderMappingFields? header = null;
        var hasHeader = woId.HasValue && woIdToHeaderFields.TryGetValue(woId.Value, out header);
        var h = hasHeader ? header : null;

        static bool IsNullish(JsonElement v) => v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;

        static string NormalizeString(string? s) => string.IsNullOrWhiteSpace(s) ? string.Empty : s!;

        static string ReadStringLoose(JsonElement v)
        {
            if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? string.Empty;
            if (IsNullish(v)) return string.Empty;
            return v.ToString() ?? string.Empty;
        }

        static decimal ReadDecimalLoose(JsonElement v)
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d))
                return d;

            if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), out var ds))
                return ds;

            return 0m;
        }

        static string PreferHeaderString(string existing, string? headerValue)
            => !string.IsNullOrWhiteSpace(existing) ? existing : NormalizeString(headerValue);

        static decimal PreferHeaderDecimal(decimal existing, decimal? headerValue)
            => existing != 0m ? existing : (headerValue ?? 0m);

        static string ToFscmDateLiteralOrEmpty(DateTime? dtUtc)
        {
            if (!dtUtc.HasValue) return MissingDateSentinel;

            var dt = dtUtc.Value;
            if (dt.Kind == DateTimeKind.Local)
                dt = dt.ToUniversalTime();
            else if (dt.Kind == DateTimeKind.Unspecified)
                dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);

            var ms = new DateTimeOffset(dt).ToUnixTimeMilliseconds();
            return $"/Date({ms})/";
        }

        // Required keys (strings) that must always be present and never null.
        // If payload has empty -> prefer header-map.
        var requiredStringKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Raw FS logical names (requested)
        "rpc_productlines",
        "rpc_departments",
        "msdyn_timefrompromised",
        "msdyn_timetopromised",
        "rpc_ponumber",
        "rpc_utcactualstartdate",
        "rpc_utcactualenddate",
        "rpc_countrylookup",
        "rpc_countylookup",
        "rpc_statelookup",
        "rpc_invoicenotesinternal",
        "rpc_invoicenotesexternal",
        "rpc_declinedtosignreason",
        "Work Type",
        "Well Age",
        "Taxability Type",

        // Canonical outbound names
        "ActualStartDate",
        "ActualEndDate",
        "ProjectedStartDate",
        "ProjectedEndDate",
        "CountryRegionId",
        "County",
        "State",
        "FSACustomerReference",
        "FSADeclinedToSign",
        "FSATaxabilityType",
        "FSAWorkType",
        "FSAWellAge",
        "InvoiceNotesInternal",
        "InvoiceNotesExternal",

        // Back-compat keys (emit once)
        "FSAInvoiceNotesInternal",
        "FSAInvoiceNotesExternal"
    };

        // Required numeric keys (must always exist, never null; default 0)
        var requiredNumberKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Latitude",
        "Longitude"
    };

        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Small helpers to write + mark written
        void WriteString(string name, string value)
        {
            w.WriteString(name, value);
            written.Add(name);
        }

        void WriteNumber(string name, decimal value)
        {
            w.WriteNumber(name, value);
            written.Add(name);
        }

        // Given a required string key and existing payload value, compute final based on header-map.
        void WriteRequiredString(string name, JsonElement existingValue)
        {
            var existing = ReadStringLoose(existingValue);
            if (IsNullish(existingValue)) existing = string.Empty;

            // Map header-map source by key
            string? headerValue = name switch
            {
                // FS logical names
                "rpc_productlines" => h?.ProductLine,
                "rpc_departments" => h?.Department,
                "rpc_ponumber" => h?.PONumber,
                "rpc_countrylookup" => h?.Country,
                "rpc_countylookup" => h?.County,
                "rpc_statelookup" => h?.State,
                "rpc_invoicenotesinternal" => h?.InvoiceNotesInternal,
                "rpc_invoicenotesexternal" => h?.InvoiceNotesExternal,
                "rpc_declinedtosignreason" => h?.DeclinedToSignReason,
                "Work Type" => h?.FSAWorkType,
                "Well Age" => h?.FSAWellAge,
                "Taxability Type" => h?.FSATaxabilityType,

                // Canonical outbound keys
                "CountryRegionId" => h?.Country,
                "County" => h?.County,
                "State" => h?.State,
                "FSACustomerReference" => h?.PONumber,
                "FSADeclinedToSign" => h?.DeclinedToSignReason,
                "FSATaxabilityType" => h?.FSATaxabilityType,
                "FSAWorkType" => h?.FSAWorkType,
                "FSAWellAge" => h?.FSAWellAge,
                "InvoiceNotesInternal" => h?.InvoiceNotesInternal,
                "InvoiceNotesExternal" => h?.InvoiceNotesExternal,
                "FSAInvoiceNotesInternal" => h?.InvoiceNotesInternal,
                "FSAInvoiceNotesExternal" => h?.InvoiceNotesExternal,

                // Dates handled separately (must be /Date(ms)/)
                "ActualStartDate" => null,
                "ActualEndDate" => null,
                "ProjectedStartDate" => null,
                "ProjectedEndDate" => null,
                "msdyn_timefrompromised" => null,
                "msdyn_timetopromised" => null,
                "rpc_utcactualstartdate" => null,
                "rpc_utcactualenddate" => null,

                _ => null
            };

            // Date keys: output FSCM literal, prefer existing if it is already non-empty
            if (name.Equals("ActualStartDate", StringComparison.OrdinalIgnoreCase))
            {
                var final = !string.IsNullOrWhiteSpace(existing) ? existing : ToFscmDateLiteralOrEmpty(h?.ActualStartDateUtc);
                WriteString("ActualStartDate", final);
                return;
            }
            if (name.Equals("ActualEndDate", StringComparison.OrdinalIgnoreCase))
            {
                var final = !string.IsNullOrWhiteSpace(existing) ? existing : ToFscmDateLiteralOrEmpty(h?.ActualEndDateUtc);
                WriteString("ActualEndDate", final);
                return;
            }
            if (name.Equals("ProjectedStartDate", StringComparison.OrdinalIgnoreCase))
            {
                var final = !string.IsNullOrWhiteSpace(existing) ? existing : ToFscmDateLiteralOrEmpty(h?.ProjectedStartDateUtc);
                WriteString("ProjectedStartDate", final);
                return;
            }
            if (name.Equals("ProjectedEndDate", StringComparison.OrdinalIgnoreCase))
            {
                var final = !string.IsNullOrWhiteSpace(existing) ? existing : ToFscmDateLiteralOrEmpty(h?.ProjectedEndDateUtc);
                WriteString("ProjectedEndDate", final);
                return;
            }

            // Also ensure the raw FS datetime keys exist (as strings) but we keep them as existing unless empty.
            // If empty, we emit ISO yyyy-MM-dd to satisfy the "always present" contract without changing other consumers.
            // (These are NOT used by FSCM posting; canonical keys above are.)
            if (name.Equals("msdyn_timefrompromised", StringComparison.OrdinalIgnoreCase))
            {
                var final = !string.IsNullOrWhiteSpace(existing)
                    ? existing
                    : (h?.ProjectedStartDateUtc.HasValue == true ? h.ProjectedStartDateUtc.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
                WriteString("msdyn_timefrompromised", final);
                return;
            }
            if (name.Equals("msdyn_timetopromised", StringComparison.OrdinalIgnoreCase))
            {
                var final = !string.IsNullOrWhiteSpace(existing)
                    ? existing
                    : (h?.ProjectedEndDateUtc.HasValue == true ? h.ProjectedEndDateUtc.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
                WriteString("msdyn_timetopromised", final);
                return;
            }
            if (name.Equals("rpc_utcactualstartdate", StringComparison.OrdinalIgnoreCase))
            {
                var final = !string.IsNullOrWhiteSpace(existing)
                    ? existing
                    : (h?.ActualStartDateUtc.HasValue == true ? h.ActualStartDateUtc.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
                WriteString("rpc_utcactualstartdate", final);
                return;
            }
            if (name.Equals("rpc_utcactualenddate", StringComparison.OrdinalIgnoreCase))
            {
                var final = !string.IsNullOrWhiteSpace(existing)
                    ? existing
                    : (h?.ActualEndDateUtc.HasValue == true ? h.ActualEndDateUtc.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
                WriteString("rpc_utcactualenddate", final);
                return;
            }

            // Non-date string keys: prefer header-map when existing is empty
            var finalValue = PreferHeaderString(existing, headerValue);
            WriteString(name, finalValue);
        }

        // Given a required number key and existing payload value, compute final based on header-map.
        void WriteRequiredNumber(string name, JsonElement existingValue)
        {
            var existing = ReadDecimalLoose(existingValue);

            decimal? headerValue = name switch
            {
                "Latitude" => h?.WellLatitude,
                "Longitude" => h?.WellLongitude,
                _ => null
            };

            var finalValue = PreferHeaderDecimal(existing, headerValue);
            WriteNumber(name, finalValue);
        }

        w.WriteStartObject();

        // Pass 1: copy all existing properties, intercept required keys so we can apply "prefer header when empty"
        foreach (var p in wo.EnumerateObject())
        {
            if (requiredNumberKeys.Contains(p.Name))
            {
                WriteRequiredNumber(p.Name, p.Value);
                continue;
            }

            if (requiredStringKeys.Contains(p.Name))
            {
                WriteRequiredString(p.Name, p.Value);
                continue;
            }

            // Non-required key: pass-through
            w.WritePropertyName(p.Name);
            p.Value.WriteTo(w);
        }

        // Pass 2: emit any required keys that were missing entirely from the source payload
        // FS logical names
        if (!written.Contains("rpc_productlines")) WriteString("rpc_productlines", NormalizeString(h?.ProductLine));
        if (!written.Contains("rpc_departments")) WriteString("rpc_departments", NormalizeString(h?.Department));
        if (!written.Contains("msdyn_timefrompromised"))
            WriteString("msdyn_timefrompromised", h?.ProjectedStartDateUtc.HasValue == true ? h.ProjectedStartDateUtc.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
        if (!written.Contains("msdyn_timetopromised"))
            WriteString("msdyn_timetopromised", h?.ProjectedEndDateUtc.HasValue == true ? h.ProjectedEndDateUtc.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
        if (!written.Contains("rpc_ponumber")) WriteString("rpc_ponumber", NormalizeString(h?.PONumber));
        if (!written.Contains("rpc_utcactualstartdate"))
            WriteString("rpc_utcactualstartdate", h?.ActualStartDateUtc.HasValue == true ? h.ActualStartDateUtc.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
        if (!written.Contains("rpc_utcactualenddate"))
            WriteString("rpc_utcactualenddate", h?.ActualEndDateUtc.HasValue == true ? h.ActualEndDateUtc.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
        if (!written.Contains("rpc_countrylookup")) WriteString("rpc_countrylookup", NormalizeString(h?.Country));
        if (!written.Contains("rpc_countylookup")) WriteString("rpc_countylookup", NormalizeString(h?.County));
        if (!written.Contains("rpc_statelookup")) WriteString("rpc_statelookup", NormalizeString(h?.State));
        if (!written.Contains("rpc_invoicenotesinternal")) WriteString("rpc_invoicenotesinternal", NormalizeString(h?.InvoiceNotesInternal));
        if (!written.Contains("rpc_invoicenotesexternal")) WriteString("rpc_invoicenotesexternal", NormalizeString(h?.InvoiceNotesExternal));
        if (!written.Contains("rpc_declinedtosignreason")) WriteString("rpc_declinedtosignreason", NormalizeString(h?.DeclinedToSignReason));
        if (!written.Contains("Work Type")) WriteString("Work Type", NormalizeString(h?.FSAWorkType));
        if (!written.Contains("Well Age")) WriteString("Well Age", NormalizeString(h?.FSAWellAge));
        if (!written.Contains("Taxability Type")) WriteString("Taxability Type", NormalizeString(h?.FSATaxabilityType));

        // Canonical outbound keys
        if (!written.Contains("ActualStartDate")) WriteString("ActualStartDate", ToFscmDateLiteralOrEmpty(h?.ActualStartDateUtc));
        if (!written.Contains("ActualEndDate")) WriteString("ActualEndDate", ToFscmDateLiteralOrEmpty(h?.ActualEndDateUtc));
        if (!written.Contains("ProjectedStartDate")) WriteString("ProjectedStartDate", ToFscmDateLiteralOrEmpty(h?.ProjectedStartDateUtc));
        if (!written.Contains("ProjectedEndDate")) WriteString("ProjectedEndDate", ToFscmDateLiteralOrEmpty(h?.ProjectedEndDateUtc));

        if (!written.Contains("Latitude")) WriteNumber("Latitude", h?.WellLatitude ?? 0m);
        if (!written.Contains("Longitude")) WriteNumber("Longitude", h?.WellLongitude ?? 0m);

        if (!written.Contains("CountryRegionId")) WriteString("CountryRegionId", NormalizeString(h?.Country));
        if (!written.Contains("County")) WriteString("County", NormalizeString(h?.County));
        if (!written.Contains("State")) WriteString("State", NormalizeString(h?.State));
        if (!written.Contains("FSACustomerReference")) WriteString("FSACustomerReference", NormalizeString(h?.PONumber));
        if (!written.Contains("FSADeclinedToSign")) WriteString("FSADeclinedToSign", NormalizeString(h?.DeclinedToSignReason));
        if (!written.Contains("FSATaxabilityType")) WriteString("FSATaxabilityType", NormalizeString(h?.FSATaxabilityType));
        if (!written.Contains("FSAWorkType")) WriteString("FSAWorkType", NormalizeString(h?.FSAWorkType));
        if (!written.Contains("FSAWellAge")) WriteString("FSAWellAge", NormalizeString(h?.FSAWellAge));
        if (!written.Contains("InvoiceNotesInternal")) WriteString("InvoiceNotesInternal", NormalizeString(h?.InvoiceNotesInternal));
        if (!written.Contains("InvoiceNotesExternal")) WriteString("InvoiceNotesExternal", NormalizeString(h?.InvoiceNotesExternal));

        // Back-compat keys (emit once)
        if (!written.Contains("FSAInvoiceNotesInternal")) WriteString("FSAInvoiceNotesInternal", NormalizeString(h?.InvoiceNotesInternal));
        if (!written.Contains("FSAInvoiceNotesExternal")) WriteString("FSAInvoiceNotesExternal", NormalizeString(h?.InvoiceNotesExternal));

        w.WriteEndObject();
    }

    private static void WriteIsoDateIfPresent(Utf8JsonWriter w, string propName, DateTime? dtUtc)
    {
        if (!dtUtc.HasValue) return;

        var dt = dtUtc.Value;
        if (dt.Kind == DateTimeKind.Local)
            dt = dt.ToUniversalTime();
        else if (dt.Kind == DateTimeKind.Unspecified)
            dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);

        // Output yyyy-MM-dd
        w.WriteString(propName, dt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string BuildDefaultDimensionDisplayValue(string? department, string? productLine)
    {
        static string? Clean(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return s.Trim();
        }

        var dept = Clean(department) ?? string.Empty;
        var prod = Clean(productLine) ?? string.Empty;

        var segments = new[]
        {
                string.Empty,
                dept,
                prod,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty
            };

        return string.Join("-", segments);
    }
}
