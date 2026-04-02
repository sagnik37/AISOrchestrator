using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.JournalPolicies;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;

/// <summary>
/// Defines WO journal projection behavior.
/// </summary>
public interface IWoJournalProjector
{
    ProjectionResult Project(string normalizedWoPayloadJson, JournalType journalType);
}

/// <summary>
/// Carries projection result data.
/// </summary>
public sealed record ProjectionResult(
    string PayloadJson,
    int WorkOrdersBefore,
    int WorkOrdersAfter,
    int RemovedDueToMissingOrEmptySection);

/// <summary>
/// Projects a normalized WO payload down to a single journal type section
/// (Item/Expense/Hour), pruning work orders where that section is missing or empty.
///
/// OCP: journal section key resolution is delegated to <see cref="IJournalTypePolicyResolver"/>
/// so adding a new journal type is additive (policy + DI), not a switch edit.
/// </summary>
public sealed class WoJournalProjector : IWoJournalProjector
{
    private const string JournalLinesKey = WoPayloadJsonToolkit.JournalLinesKey; // "JournalLines"

    private readonly IJournalTypePolicyResolver _policyResolver;
    private readonly IReadOnlyList<string> _allSectionKeys;

    public WoJournalProjector(IJournalTypePolicyResolver policyResolver, IEnumerable<IJournalTypePolicy> policies)
    {
        _policyResolver = policyResolver ?? throw new ArgumentNullException(nameof(policyResolver));

        // Keep a stable list of known section keys for pruning.
        // If  add a new journal type, registering its policy automatically updates this list.
        _allSectionKeys = (policies ?? Array.Empty<IJournalTypePolicy>())
            .Select(p => p.SectionKey)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Executes project.
    /// </summary>
    public ProjectionResult Project(string normalizedWoPayloadJson, JournalType journalType)
    {
        if (string.IsNullOrWhiteSpace(normalizedWoPayloadJson))
            throw new ArgumentException("Payload is empty.", nameof(normalizedWoPayloadJson));

        var before = GetWoCountOrZero(normalizedWoPayloadJson);

        var root = JsonNode.Parse(normalizedWoPayloadJson) as JsonObject
            ?? throw new InvalidOperationException("Payload is not a JSON object.");

        if (root[WoPayloadJsonToolkit.RequestKey] is not JsonObject req)
            throw new InvalidOperationException($"Missing '{WoPayloadJsonToolkit.RequestKey}'.");

        if (req[WoPayloadJsonToolkit.WoListKey] is not JsonArray wolist)
            throw new InvalidOperationException($"Missing '{WoPayloadJsonToolkit.WoListKey}'.");

        var keepKey = _policyResolver.Resolve(journalType).SectionKey;

        var removed = 0;
        var newList = new JsonArray();
        foreach (var woNode in wolist)
        {
            if (woNode is not JsonObject woObj)
                continue;

            if (woObj[keepKey] is not JsonObject section)
            {
                removed++;
                continue;
            }

            if (section[JournalLinesKey] is not JsonArray lines || lines.Count == 0)
            {
                removed++;
                continue;
            }

            // :
            // JsonNode instances can only belong to a single parent.
            // "woObj" is already parented to "wolist"; adding it to "newList" would throw:
            // System.InvalidOperationException: The node already has a parent.
            // Therefore we must clone before adding to the new array.
            var projectedWo = (JsonObject)woObj.DeepClone();

            // Prune all other journal sections for this work order (if present).
            foreach (var k in _allSectionKeys)
            {
                if (!string.Equals(k, keepKey, StringComparison.Ordinal))
                    projectedWo.Remove(k);
            }

            newList.Add(projectedWo);
        }

        req[WoPayloadJsonToolkit.WoListKey] = newList;
        var projectedJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

        var after = newList.Count;
        return new ProjectionResult(projectedJson, before, after, removed);
    }

    /// <summary>
    /// Executes get wo count or zero.
    /// </summary>
    private static int GetWoCountOrZero(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(WoPayloadJsonToolkit.RequestKey, out var req) || req.ValueKind != JsonValueKind.Object)
                return 0;
            if (!req.TryGetProperty(WoPayloadJsonToolkit.WoListKey, out var wolist) || wolist.ValueKind != JsonValueKind.Array)
                return 0;
            return wolist.GetArrayLength();
        }
        catch
        {
            return 0;
        }
    }
}
