using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Tests.TestDoubles;

using Xunit;

namespace Rpc.AIS.Accrual.Orchestrator.Tests.Integration;

public sealed class FscmJournalPosterContractTests
{
    [Fact]
    public async Task Validate_5xx_blocks_create_and_post()
    {
        var requests = new List<HttpRequestMessage>();

        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            requests.Add(req);

            // First call is validate => 500
            return Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, @"{""error"":""boom""}"));
        });

        var http = new HttpClient(handler);

        var poster = BuildPoster(http, new FscmOptions
        {
            BaseUrl = "https://fscm.example",
            JournalValidatePath = "/api/validate",
            JournalCreatePath = "/api/create",
            JournalPostPath = "/api/post"
        });

        var ctx = new RunContext(
            RunId: Guid.NewGuid().ToString("N"),
            StartedAtUtc: DateTimeOffset.UtcNow,
            TriggeredBy: "Post",
            CorrelationId: "corr");

        var payload = MinimalWoPayload(company: "425", subProjectId: "425-P0000100-00011");

        var outcome = await poster.PostAsync(ctx, JournalType.Expense, payload, CancellationToken.None);

        outcome.IsSuccessStatusCode.Should().BeFalse();
        ((int)outcome.StatusCode).Should().Be(500);

        requests.Should().HaveCount(1);
        requests[0].RequestUri!.ToString().Should().Be("https://fscm.example/api/validate");
    }

    [Fact]
    public async Task Timer_trigger_skips_posting_calls_only_validate_and_create()
    {
        var calls = new List<string>();

        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            calls.Add(req.RequestUri!.ToString());

            if (calls.Count == 1) // validate
                return Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, @"{""ok"":true}"));

            // create
            return Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, CreateResponseJson(journalId: "425-JNUM-00000191")));
        });

        var http = new HttpClient(handler);

        var poster = BuildPoster(http, new FscmOptions
        {
            BaseUrl = "https://fscm.example",
            JournalValidatePath = "/api/validate",
            JournalCreatePath = "/api/create",
            JournalPostPath = "/api/post"
        });

        var ctx = new RunContext(
            RunId: Guid.NewGuid().ToString("N"),
            StartedAtUtc: DateTimeOffset.UtcNow,
            TriggeredBy: "Timer",
            CorrelationId: "corr");

        var payload = MinimalWoPayload(company: "425", subProjectId: "425-P0000100-00011");

        var outcome = await poster.PostAsync(ctx, JournalType.Expense, payload, CancellationToken.None);

        outcome.IsSuccessStatusCode.Should().BeTrue();
        calls.Should().HaveCount(2);
        calls[0].Should().Be("https://fscm.example/api/validate");
        calls[1].Should().Be("https://fscm.example/api/create");
    }

    [Fact]
    public async Task Post_trigger_posts_journals_as_journallist_array()
    {
        var calls = new List<(string Url, string Body)>();

        var handler = new FakeHttpMessageHandler(async (req, ct) =>
        {
            var body = req.Content is null ? "" : await req.Content.ReadAsStringAsync(ct);
            calls.Add((req.RequestUri!.ToString(), body));

            if (calls.Count == 1) // validate
                return FakeHttpMessageHandler.Json(HttpStatusCode.OK, @"{""ok"":true}");

            if (calls.Count == 2) // create
                return FakeHttpMessageHandler.Json(HttpStatusCode.OK, CreateResponseJson(journalId: "425-JNUM-00000191"));

            // post
            return FakeHttpMessageHandler.Json(HttpStatusCode.OK, @"{""ok"":true}");
        });

        var http = new HttpClient(handler);

        var poster = BuildPoster(http, new FscmOptions
        {
            BaseUrl = "https://fscm.example",
            JournalValidatePath = "/api/validate",
            JournalCreatePath = "/api/create",
            JournalPostPath = "/api/post"
        });

        var ctx = new RunContext(
            RunId: Guid.NewGuid().ToString("N"),
            StartedAtUtc: DateTimeOffset.UtcNow,
            TriggeredBy: "Post",
            CorrelationId: "corr");

        var payload = MinimalWoPayload(company: "425", subProjectId: "425-P0000100-00011");

        var outcome = await poster.PostAsync(ctx, JournalType.Expense, payload, CancellationToken.None);

        outcome.IsSuccessStatusCode.Should().BeTrue();

        calls.Select(c => c.Url).Should().BeEquivalentTo(new[]
        {
            "https://fscm.example/api/validate",
            "https://fscm.example/api/create",
            "https://fscm.example/api/post"
        }, opts => opts.WithStrictOrdering());

        // Validate the post body contains JournalList array
        var postBody = calls[2].Body;
        using var doc = JsonDocument.Parse(postBody);
        doc.RootElement.TryGetProperty("_request", out var reqEl).Should().BeTrue();

        reqEl.TryGetProperty("JournalList", out var listEl).Should().BeTrue();
        listEl.ValueKind.Should().Be(JsonValueKind.Array);
        listEl.GetArrayLength().Should().BeGreaterThan(0);

        var first = listEl.EnumerateArray().First();
        // NOTE: The DTO uses [JsonPropertyName] and may be lower-case (company/journalId/journalType).
        // Assert case-insensitively.
        GetPropCI(first, "Company", "company").GetString().Should().Be("425");
        GetPropCI(first, "JournalId", "journalId").GetString().Should().Be("425-JNUM-00000191");
        GetPropCI(first, "JournalType", "journalType").GetString().Should().Be("Expense");
    }

    private static JsonElement GetPropCI(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var v))
                return v;

            // Manual case-insensitive scan (JsonElement.TryGetProperty is case-sensitive)
            foreach (var p in obj.EnumerateObject())
                if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return p.Value;
        }

        throw new KeyNotFoundException($"Missing expected property. Tried: {string.Join(", ", names)}");
    }

    private static FscmJournalPoster BuildPoster(HttpClient http, FscmOptions opt)
    {
        var reqFactory = new FscmPostRequestFactory();
        var exec = new PassThroughResilientHttpExecutor();
        var aisLogger = new NoopAisLogger();

        var diag = new FakeAisDiagnosticsOptions();

        var periodClient = new AlwaysOpenAccountingPeriodClient();
        var dateAdjuster = new PayloadPostingDateAdjuster(periodClient, NullLogger<PayloadPostingDateAdjuster>.Instance);

        return new FscmJournalPoster(
            http,
            opt,
            reqFactory,
            exec,
            dateAdjuster,
            aisLogger,
            diag,
            NullLogger<FscmJournalPoster>.Instance);
    }

    private static string MinimalWoPayload(string company, string subProjectId)
        => $@"{{
  ""_request"": {{
    ""WOList"": [
      {{
        ""Company"": ""{company}"",
        ""SubProjectId"": ""{subProjectId}"",
        ""WorkOrderGUID"": ""{{11111111-1111-1111-1111-111111111111}}"",
        ""WorkOrderID"": ""J-CPS-000001310"",
        ""WOExpLines"": {{
          ""JournalDescription"": ""x"",
          ""JournalName"": ""ProjExp"",
          ""LineType"": ""Expense"",
          ""JournalLines"": []
        }}
      }}
    ]
  }}
}}";

    private static string CreateResponseJson(string journalId)
        => $@"{{
  ""WOList"": [
    {{
      ""WOExpLines"": {{
        ""JournalId"": ""{journalId}""
      }}
    }}
  ]
}}";
}