// File: FsaLineFetcherWorkflow.TaxabilityEnrichment.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

public sealed partial class FsaLineFetcherWorkflow
{
    private const string OperationTypeLookupField = "_rpc_operationtype_value";
    private const string OperationTypeTaxabilityEntitySet = "rpc_operationtypetaxabilitytypes";
    private const string OperationTypeTaxabilityIdField = "rpc_operationtypetaxabilitytypeid";
    private const string OperationTypeTaxabilityOperationLookupField = "_rpc_operationtype_value";
    private const string OperationTypeTaxabilityTaxLookupField = "_rpc_taxabilitytype_value";

    private const string PayloadTaxabilityKey = "Taxability Type";
    private const string PayloadWorkTypeKey = "Work Type";
    private const string PayloadWellAgeKey = "Well Age";

    private const string WorkTypeLookupField = "_rpc_worktypelookup_value";
    private const string WorkTypeEntitySet = "rpc_worktypes";
    private const string WorkTypeIdField = "rpc_worktypeid";
    private const string WorkTypeWellAgeField = "rpc_wellage";
    private const string WorkTypeWellTypeField = "rpc_welltype";

    /// <summary>
    /// Enrich work orders with Work Type + Well Age.
    ///
    /// Previous behavior split the formatted lookup name using a configured separator.
    /// That approach was brittle and caused "Well Age" / "Work Type" mix-ups.
    ///
    /// New behavior:
    /// - Read the work type lookup id from msdyn_workorder (_rpc_worktypelookup_value)
    /// - Fetch rpc_worktype rows for those ids
    /// - Use rpc_welltype -> "Work Type" and rpc_wellage -> "Well Age"
    /// </summary>
    internal async Task<JsonDocument> EnrichWorkOrdersWithWorkTypeFieldsAsync(JsonDocument doc, CancellationToken ct)
    {
        if (doc is null) return doc;
        if (!doc.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return doc;

        // Collect distinct worktype ids.
        var workTypeIds = new HashSet<Guid>();
        foreach (var row in arr.EnumerateArray())
        {
            if (TryGuid(row, WorkTypeLookupField, out var wtId) && wtId != Guid.Empty)
                workTypeIds.Add(wtId);
        }

        if (workTypeIds.Count == 0)
            return doc;

        // Fetch rpc_worktype details.
        using var wtDoc = await AggregateByIdsAsync(
            entitySetName: WorkTypeEntitySet,
            idProperty: WorkTypeIdField,
            ids: workTypeIds.ToList(),
            select: string.Join(",", WorkTypeIdField, WorkTypeWellAgeField, WorkTypeWellTypeField),
            expand: null,
            orderBy: null,
            chunkSize: (_opt.OrFilterChunkSize > 0 ? _opt.OrFilterChunkSize : DefaultChunkSize),
            ct: ct).ConfigureAwait(false);

        var wtById = new Dictionary<Guid, (string? WellAge, string? WellType)>();
        if (wtDoc.RootElement.TryGetProperty("value", out var wtArr) && wtArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in wtArr.EnumerateArray())
            {
                if (!TryGuid(row, WorkTypeIdField, out var id) || id == Guid.Empty)
                    continue;

                // These are option set fields in Dataverse.
                // Prefer formatted value annotation when present, otherwise fall back to string/raw numeric.
                static string? ReadOptionSetFormatted(JsonElement obj, string fieldName)
                {
                    // Formatted value annotation: <field>@OData.Community.Display.V1.FormattedValue
                    var formattedName = fieldName + "@OData.Community.Display.V1.FormattedValue";
                    if (obj.TryGetProperty(formattedName, out var fv) && fv.ValueKind == JsonValueKind.String)
                        return fv.GetString();

                    if (obj.TryGetProperty(fieldName, out var v))
                    {
                        if (v.ValueKind == JsonValueKind.String) return v.GetString();
                        if (v.ValueKind == JsonValueKind.Number) return v.GetRawText(); // last-resort (shouldn't happen if Prefer header applied)
                    }

                    return null;
                }

                var wellAge = ReadOptionSetFormatted(row, WorkTypeWellAgeField);
                var wellType = ReadOptionSetFormatted(row, WorkTypeWellTypeField);

                wtById[id] = (WellAge: wellAge, WellType: wellType);
            }
        }

        if (wtById.Count == 0)
            return doc;

        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("value"))
                {
                    w.WritePropertyName("value");
                    w.WriteStartArray();

                    foreach (var row in arr.EnumerateArray())
                    {
                        w.WriteStartObject();
                        foreach (var c in row.EnumerateObject())
                            c.WriteTo(w);

                        if (TryGuid(row, WorkTypeLookupField, out var wtId) && wtId != Guid.Empty &&
                            wtById.TryGetValue(wtId, out var details))
                        {
                            if (!string.IsNullOrWhiteSpace(details.WellType))
                            {
                                w.WriteString(PayloadWorkTypeKey, details.WellType);
                                // Back-compat: older mapping reads from "Well Name".
                                // Keep writing it to avoid breaking existing payload consumers.
                                w.WriteString("Well Name", details.WellType);
                            }

                            if (!string.IsNullOrWhiteSpace(details.WellAge))
                                w.WriteString(PayloadWellAgeKey, details.WellAge);
                        }

                        w.WriteEndObject();
                    }

                    w.WriteEndArray();
                }
                else
                {
                    prop.WriteTo(w);
                }
            }
            w.WriteEndObject();
        }

        stream.Position = 0;
        return JsonDocument.Parse(stream);
    }

    private async Task<JsonDocument> EnrichWithTaxabilityTypeAsync(JsonDocument doc, CancellationToken ct)
    {
        if (doc is null) return doc;
        if (!doc.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return doc;

        // Gather distinct operation type (incident type) ids from the rows.
        var opTypeIds = new HashSet<Guid>();
        foreach (var row in arr.EnumerateArray())
        {
            if (TryGuid(row, OperationTypeLookupField, out var opId) && opId != Guid.Empty)
                opTypeIds.Add(opId);
        }

        if (opTypeIds.Count == 0)
            return doc;

        // 1) OperationType -> TaxabilityType (via rpc_operationtypetaxabilitytype mapping table)
        // Read the formatted value of the lookup field rpc_taxabilitytype.
        var operationTypeTaxabilityDoc = await AggregateByIdsAsync(
            entitySetName: OperationTypeTaxabilityEntitySet,
            idProperty: OperationTypeTaxabilityOperationLookupField,
            ids: opTypeIds.ToList(),
            select: string.Join(",", OperationTypeTaxabilityIdField, OperationTypeTaxabilityOperationLookupField, OperationTypeTaxabilityTaxLookupField),
            expand: null,
            orderBy: null,
            chunkSize: (_opt.OrFilterChunkSize > 0 ? _opt.OrFilterChunkSize : DefaultChunkSize),
            ct: ct).ConfigureAwait(false);

        var taxNameByOperationTypeId = new Dictionary<Guid, string>();
        if (operationTypeTaxabilityDoc.RootElement.TryGetProperty("value", out var ottArr) && ottArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in ottArr.EnumerateArray())
            {
                if (!TryGuid(row, OperationTypeTaxabilityOperationLookupField, out var operationTypeId) || operationTypeId == Guid.Empty)
                    continue;

                if (taxNameByOperationTypeId.ContainsKey(operationTypeId))
                    continue;

                var formattedLookupName = OperationTypeTaxabilityTaxLookupField + "@OData.Community.Display.V1.FormattedValue";
                if (row.TryGetProperty(formattedLookupName, out var formattedEl) && formattedEl.ValueKind == JsonValueKind.String)
                {
                    var taxName = formattedEl.GetString();
                    if (!string.IsNullOrWhiteSpace(taxName))
                    {
                        taxNameByOperationTypeId[operationTypeId] = taxName.Trim();
                        continue;
                    }
                }
            }
        }

        if (taxNameByOperationTypeId.Count == 0)
            return doc;

        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("value"))
                {
                    w.WritePropertyName("value");
                    w.WriteStartArray();
                    foreach (var row in arr.EnumerateArray())
                    {
                        w.WriteStartObject();
                        foreach (var c in row.EnumerateObject())
                            c.WriteTo(w);

                        if (TryGuid(row, OperationTypeLookupField, out var opId) && opId != Guid.Empty &&
                            taxNameByOperationTypeId.TryGetValue(opId, out var taxName) &&
                            !string.IsNullOrWhiteSpace(taxName))
                        {
                            w.WriteString(PayloadTaxabilityKey, taxName);
                            w.WriteString("FSATaxabilityType", taxName);
                        }

                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                }
                else
                {
                    prop.WriteTo(w);
                }
            }
            w.WriteEndObject();
        }

        stream.Position = 0;
        return JsonDocument.Parse(stream);
    }
}
