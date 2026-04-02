// File: src/Rpc.AIS.Accrual.Orchestrator.Core/Services/WoPayloadValidationPipeline/WoPayloadJsonBuilder.cs

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services.WoPayloadValidationPipeline;

internal static class WoPayloadJsonBuilder
{
    internal static string BuildFilteredPayloadJson(IReadOnlyList<FilteredWorkOrder> workOrders)
    {
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        // root
        writer.WriteStartObject();

        // _request
        writer.WritePropertyName("_request");
        writer.WriteStartObject();

        // WOList: []
        writer.WritePropertyName("WOList");
        writer.WriteStartArray();

        foreach (var wo in workOrders)
        {
            if (wo.WorkOrder.ValueKind != JsonValueKind.Object)
                continue;

            if (string.IsNullOrWhiteSpace(wo.SectionKey))
                throw new InvalidOperationException("FilteredWorkOrder.SectionKey was null/empty. A journal policy SectionKey is misconfigured or missing.");

            writer.WriteStartObject();

            foreach (var prop in wo.WorkOrder.EnumerateObject())
            {
                if (string.Equals(prop.Name, wo.SectionKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                prop.WriteTo(writer);
            }

            writer.WritePropertyName(wo.SectionKey);
            writer.WriteStartObject();

            if (wo.WorkOrder.TryGetProperty(wo.SectionKey, out var section) && section.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in section.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "JournalLines", StringComparison.OrdinalIgnoreCase))
                        continue;

                    prop.WriteTo(writer);
                }
            }

            writer.WritePropertyName("JournalLines");
            writer.WriteStartArray();
            foreach (var ln in wo.Lines)
                ln.WriteTo(writer);
            writer.WriteEndArray();

            writer.WriteEndObject(); // section
            writer.WriteEndObject(); // wo
        }

        writer.WriteEndArray();   // WOList
        writer.WriteEndObject();  // _request
        writer.WriteEndObject();  // root

        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
