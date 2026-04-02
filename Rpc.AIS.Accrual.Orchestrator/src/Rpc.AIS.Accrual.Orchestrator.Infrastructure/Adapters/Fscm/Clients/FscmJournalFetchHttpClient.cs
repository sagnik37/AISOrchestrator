// File: Rpc.AIS.Accrual.Orchestrator/src/Rpc.AIS.Accrual.Orchestrator.Infrastructure/Clients/FscmJournalFetchHttpClient.cs
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.FscmJournalPolicies;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// FSCM OData fetch client to retrieve journal lines by WorkOrder GUID.
/// Maps multiple entity sets (Item/Expense/Hour) into a normalized FscmJournalLine DTO.
/// </summary>
public sealed partial class FscmJournalFetchHttpClient : IFscmJournalFetchClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly FscmOptions _endpoints;
    private readonly ILogger<FscmJournalFetchHttpClient> _logger;
    private readonly FscmJournalFetchPolicyResolver _policyResolver;
    private readonly IFscmJournalLineRowMapper _rowMapper;

    public FscmJournalFetchHttpClient(
        HttpClient http,
        FscmOptions endpoints,
        FscmJournalFetchPolicyResolver policyResolver,
        IFscmJournalLineRowMapper rowMapper,
        ILogger<FscmJournalFetchHttpClient> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
        _policyResolver = policyResolver ?? throw new ArgumentNullException(nameof(policyResolver));
        _rowMapper = rowMapper ?? throw new ArgumentNullException(nameof(rowMapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    //  FIX (CS0535): This method matches the interface EXACTLY.
    public Task<IReadOnlyList<FscmJournalLine>> FetchByWorkOrdersAsync(
        RunContext context,
        JournalType journalType,
        IReadOnlyCollection<Guid> workOrderIds,
        CancellationToken ct)
    {
        return FetchByWorkOrdersInternalAsync(context, journalType, workOrderIds, ct, allowSelectFallback: true);
    }

    // Internal helper with the extra flag (not part of interface)
    private async Task<IReadOnlyList<FscmJournalLine>> FetchByWorkOrdersInternalAsync(
        RunContext context,
        JournalType journalType,
        IReadOnlyCollection<Guid> workOrderIds,
        CancellationToken ct,
        bool allowSelectFallback)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (workOrderIds is null) throw new ArgumentNullException(nameof(workOrderIds));

        var cleaned = workOrderIds.Where(g => g != Guid.Empty).Distinct().ToList();
        if (cleaned.Count == 0) return Array.Empty<FscmJournalLine>();

        var baseUrl = ResolveBaseUrlOrThrow();
        var policy = _policyResolver.Resolve(journalType);

        var entitySet = policy.EntitySet;
        const string workOrderIdField = "RPCWorkOrderGuid";
        const string workOrderLineIdField = "RPCWorkOrderLineGuid";

        var select = policy.Select;

        var chunkSize = _endpoints.JournalHistoryOrFilterChunkSize <= 0 ? 25 : _endpoints.JournalHistoryOrFilterChunkSize;
        var all = new List<FscmJournalLine>(capacity: Math.Min(cleaned.Count * 8, 5000));

        foreach (var chunk in Chunk(cleaned, chunkSize))
        {
            // In  env: RPCWorkOrderGuid eq <bare-guid>
            //  Do NOT wrap in guid'...' unless  env requires it. Here we keep it bare-guid
            // to match the existing single-WO implementation.select={select}
            var filter = string.Join(" or ", chunk.Select(id => $"{workOrderIdField} eq {id:D}"));

            var url =
                $"{baseUrl.TrimEnd('/')}/data/{entitySet}" +
                $"?cross-company=true&$&$filter={filter.Replace(" ", "%20")}";

            var lines = await FetchSingleUrlAsync(
                    context,
                    journalType,
                    entitySet,
                    url,
                    policy,
                    workOrderLineIdField,
                    ct,
                    allowSelectFallback: allowSelectFallback)
                .ConfigureAwait(false);

            if (lines.Count != 0)
                all.AddRange(lines);
        }

        return all;
    }
}
