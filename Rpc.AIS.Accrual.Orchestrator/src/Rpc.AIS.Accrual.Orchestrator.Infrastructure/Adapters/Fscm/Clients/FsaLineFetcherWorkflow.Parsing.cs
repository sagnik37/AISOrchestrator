// File: FsaLineFetcherWorkflow.Parsing.cs


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

    private static IReadOnlyList<Guid> ParseWorkOrderGuids(List<string> workOrderIds)
    {
        if (workOrderIds is null || workOrderIds.Count == 0)
            return Array.Empty<Guid>();

        var list = new List<Guid>(workOrderIds.Count);
        foreach (var s in workOrderIds)
        {
            if (string.IsNullOrWhiteSpace(s)) continue;

            // Accept "{GUID}" and "GUID"
            var trimmed = s.Trim().TrimStart('{').TrimEnd('}');
            if (Guid.TryParse(trimmed, out var g))
                list.Add(g);
        }

        return list;
    }

    /// <summary>
    /// Executes extract work order guid set.
    /// </summary>
    private static HashSet<string> ExtractWorkOrderGuidSet(JsonDocument presenceDoc)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (presenceDoc is null) return set;

        if (!presenceDoc.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
            return set;

        foreach (var row in value.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) continue;

            // Presence queries select: _msdyn_workorder_value
            if (row.TryGetProperty("_msdyn_workorder_value", out var wo) && wo.ValueKind == JsonValueKind.String)
            {
                var s = wo.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    var trimmed = s.Trim().TrimStart('{').TrimEnd('}');
                    if (Guid.TryParse(trimmed, out var g))
                        set.Add(g.ToString("D"));
                }
            }
        }

        return set;
    }
}
