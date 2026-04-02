using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.Validation;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Resilience;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Utilities;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;

public interface IFscmJournalPoster
{
    Task<HttpPostOutcome> PostAsync(RunContext ctx, JournalType journalType, string payloadJson, CancellationToken ct);
}

public sealed record HttpPostOutcome(HttpStatusCode StatusCode, string Body, long ElapsedMs, string Url)
{
    public bool IsSuccessStatusCode => (int)StatusCode >= 200 && (int)StatusCode <= 299;
}

public sealed partial class FscmJournalPoster : IFscmJournalPoster
{
    private static readonly HashSet<string> SkipJournalPostingTriggers = new(StringComparer.OrdinalIgnoreCase)
    {
        // Accrual/Batch triggers: create journals but do not post them.
        "Timer",
        "AdHocSingle",
        "AdHocAll",
        "AdHocBulk"
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false
        // :
        // - Do NOT set PropertyNamingPolicy here.
        // - The exact payload keys (company/journalId/journalType) should be enforced
        //   via [JsonPropertyName] on DTO properties (FscmJournalPostItem).
    };

    private readonly HttpClient _http;
    private readonly FscmOptions _endpoints;
    private readonly IFscmPostRequestFactory _reqFactory;
    private readonly IResilientHttpExecutor _executor;
    private readonly ILogger<FscmJournalPoster> _logger;
    private readonly PayloadPostingDateAdjuster _dateAdjuster;
    private readonly IAisLogger _aisLogger;
    private readonly IAisDiagnosticsOptions _diag;

    public FscmJournalPoster(
        HttpClient http,
        FscmOptions endpoints,
        IFscmPostRequestFactory reqFactory,
        IResilientHttpExecutor executor,
        PayloadPostingDateAdjuster dateAdjuster,
        IAisLogger aisLogger,
        IAisDiagnosticsOptions diag,
        ILogger<FscmJournalPoster> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
        _reqFactory = reqFactory ?? throw new ArgumentNullException(nameof(reqFactory));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _dateAdjuster = dateAdjuster ?? throw new ArgumentNullException(nameof(dateAdjuster));
        _aisLogger = aisLogger ?? throw new ArgumentNullException(nameof(aisLogger));
        _diag = diag ?? throw new ArgumentNullException(nameof(diag));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HttpPostOutcome> PostAsync(RunContext ctx, JournalType journalType, string payloadJson, CancellationToken ct)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        if (string.IsNullOrWhiteSpace(payloadJson)) throw new ArgumentException("Payload is empty.", nameof(payloadJson));

        var baseUrl = FscmUrlBuilder.ResolveFscmBaseUrl(_endpoints, _endpoints.PostingBaseUrlOverride, legacyName: "FscmPostingBaseUrl");

        // 0) Existing behavior: adjust PostingDate based on open/closed fiscal period
        payloadJson = await _dateAdjuster.AdjustAsync(ctx, payloadJson, ct).ConfigureAwait(false);

        // === NEW: Extract context once, validate per endpoint before calling FSCM ===
        var contextReq = ExtractPostingContext(payloadJson, _logger, ctx);

        // 1) Journal Validate
        if (!string.IsNullOrWhiteSpace(_endpoints.JournalValidatePath))
        {
            var fail = TryValidateEndpoint(FscmEndpointType.JournalValidate, contextReq);
            if (fail is not null) return fail;
        }

        // 2) Journal Create
        if (!string.IsNullOrWhiteSpace(_endpoints.JournalCreatePath))
        {
            var fail = TryValidateEndpoint(FscmEndpointType.JournalCreate, contextReq);
            if (fail is not null) return fail;
        }

        // 3) Journal Post (supports either new or legacy config key)
        var postPath = !string.IsNullOrWhiteSpace(_endpoints.JournalPostPath)
            ? _endpoints.JournalPostPath
            : _endpoints.JournalPostCustomPath;

        if (!string.IsNullOrWhiteSpace(postPath))
        {
            var fail = TryValidateEndpoint(FscmEndpointType.JournalPost, contextReq);
            if (fail is not null) return fail;
        }

        // === Existing workflow continues unchanged ===

        // Validate
        var validate = await CallStepAsync(ctx, "FSCM_JOURNAL_VALIDATE", baseUrl, _endpoints.JournalValidatePath, payloadJson, ct);

        if ((int)validate.StatusCode >= 500)
        {
            _logger.LogError(
                "FSCM validate returned {Status}. Blocking journal CREATE to prevent duplicates. RunId={RunId} CorrelationId={CorrelationId} JournalType={JournalType}",
                (int)validate.StatusCode, ctx.RunId, ctx.CorrelationId, journalType);

            return validate; // <- ensures CREATE never runs for 5xx
        }

        if (!validate.IsSuccessStatusCode)
            return validate;


        // Create
        var create = await CallStepAsync(ctx, "FSCM_JOURNAL_CREATE", baseUrl, _endpoints.JournalCreatePath, payloadJson, ct)
            .ConfigureAwait(false);

        if (!create.IsSuccessStatusCode)
            return create;

        // Policy: for some triggers we must NOT post journals (side-effectful POST).
        // In these flows we stop after CREATE and return the create response as the outcome.
        // The caller will treat this as success (HTTP 2xx), but WorkOrdersPosted will be forced to 0 by the outcome layer.
        if (ShouldSkipJournalPosting(ctx))
        {
            _logger.LogInformation(
                "FSCM journal POST step skipped by trigger policy. TriggeredBy={TriggeredBy} JournalType={JournalType} RunId={RunId} CorrelationId={CorrelationId}",
                ctx.TriggeredBy, journalType, ctx.RunId, ctx.CorrelationId);

            return create;
        }

        // Mandatory fields already validated above, safe to use.
        var company = contextReq.Company ?? string.Empty;

        // NEW: Extract ALL journal ids returned by create response (Item/Expense/Hour per WO)
        if (!TryExtractJournalPostsFromCreateResponse(create.Body ?? string.Empty, company, out var journalPosts, _logger, ctx) ||
            journalPosts.Count == 0)
        {
            _logger.LogError(
                "FSCM create step succeeded but did not return any JournalIds. Step={Step} RunId={RunId} CorrelationId={CorrelationId}. CreateBody={Body}",
                "FSCM_JOURNAL_CREATE", ctx.RunId, ctx.CorrelationId, LogText.TrimForLog(create.Body ?? string.Empty));

            return new HttpPostOutcome(
                HttpStatusCode.InternalServerError,
                @"{""error"":""FSCM create did not return any JournalIds; cannot post journals.""}",
                create.ElapsedMs,
                create.Url);
        }

        // NEW: Post ALL created journals in ONE call as JournalList[]
        var postPayload = JsonSerializer.Serialize(
            new FscmPostJournalRequest(
                new FscmPostJournalEnvelope(
                    journalPosts.Select(j => new FscmJournalPostItem(
                        Company: j.Company,
                        JournalId: j.JournalId,
                        JournalType: j.JournalType
                    )).ToArray())),
            JsonOpts);

        var post = await CallStepAsync(ctx, "FSCM_JOURNAL_POST", baseUrl, postPath, postPayload, ct)
            .ConfigureAwait(false);

        if (!post.IsSuccessStatusCode)
            return post;

        return post;
    }

    private static bool ShouldSkipJournalPosting(RunContext ctx)
        => !string.IsNullOrWhiteSpace(ctx.TriggeredBy) && SkipJournalPostingTriggers.Contains(ctx.TriggeredBy!);
    private sealed record JournalPostRow(string Company, string JournalId, string JournalType);
}
