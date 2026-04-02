using System;
using System.IO;
using System.Text.Json;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Flattens common Dataverse expand shapes into top-level scalar fields expected by Core.
/// This preserves the historic behavior that older FsaLineFetcher performed inline.
/// </summary>
public sealed class FsaRowFlattener : IFsaRowFlattener
{
    /// <summary>
    /// Flattens company info from the msdyn_serviceaccount expand into:
    /// - cdm_companycode (formatted value)
    /// - cdm_companyid (raw lookup guid string, if present)
    /// </summary>
    public JsonDocument FlattenWorkOrderCompanyFromExpand(JsonDocument workOrdersDoc)
    {
        if (workOrdersDoc is null) throw new ArgumentNullException(nameof(workOrdersDoc));

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            var root = workOrdersDoc.RootElement;
            writer.WriteStartObject();

            foreach (var prop in root.EnumerateObject())
            {
                if (!prop.NameEquals("value"))
                {
                    prop.WriteTo(writer);
                    continue;
                }

                writer.WritePropertyName("value");
                writer.WriteStartArray();

                foreach (var row in prop.Value.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object)
                    {
                        row.WriteTo(writer);
                        continue;
                    }

                    writer.WriteStartObject();
                    foreach (var c in row.EnumerateObject())
                        c.WriteTo(writer);

                    // Expand: msdyn_serviceaccount
                    if (row.TryGetProperty(DataverseSchema.Nav_ServiceAccount, out var svc) &&
                        svc.ValueKind == JsonValueKind.Object)
                    {
                        // formatted company code
                        var fmtKey = DataverseSchema.AccountCompanyLookupField + DataverseSchema.ODataFormattedSuffix;
                        if (svc.TryGetProperty(fmtKey, out var fmt) && fmt.ValueKind == JsonValueKind.String)
                        {
                            var code = fmt.GetString();
                            if (!string.IsNullOrWhiteSpace(code))
                                writer.WriteString(DataverseSchema.FlatCompanyCodeField, code);
                        }

                        // raw company id (GUID string)
                        if (svc.TryGetProperty(DataverseSchema.AccountCompanyLookupField, out var raw) &&
                            raw.ValueKind == JsonValueKind.String)
                        {
                            var id = raw.GetString();
                            if (!string.IsNullOrWhiteSpace(id))
                                writer.WriteString(DataverseSchema.FlatCompanyIdField, id);
                        }
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        ms.Position = 0;
        return JsonDocument.Parse(ms);
    }


}