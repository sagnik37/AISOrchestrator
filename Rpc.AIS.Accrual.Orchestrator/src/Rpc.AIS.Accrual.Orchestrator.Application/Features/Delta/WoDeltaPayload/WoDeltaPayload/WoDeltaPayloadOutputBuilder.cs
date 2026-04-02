// File: .../Core/Services/WoDeltaPayload/WoDeltaPayloadOutputBuilder.cs
// Extracted from WoDeltaPayloadService.Helpers.cs to improve SRP (output composition helpers).

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services;

using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;
using System;
using System.Text.Json.Nodes;

internal static class WoDeltaPayloadOutputBuilder
{
    internal static string BuildEmptyPayload()
        => "{\"_request\":{\"WOList\":[]}}";

    internal static void CopyIfPresentLoose(JsonObject src, JsonObject dst, string key)
    {
        if (JsonLooseKey.TryGetNodeLoose(src, key, out var v) && v is not null)
            dst[key] = v.DeepClone();
    }
}
