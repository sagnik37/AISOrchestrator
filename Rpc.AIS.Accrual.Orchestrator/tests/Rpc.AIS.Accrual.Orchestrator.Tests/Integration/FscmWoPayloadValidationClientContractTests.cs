using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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

public sealed class FscmWoPayloadValidationClientContractTests
{
    [Fact]
    public async Task ValidateAsync_skips_when_path_not_configured()
    {
        var calls = new List<string>();

        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            calls.Add(req.RequestUri!.ToString());
                        var body = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["WO Headers"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["Status"] = "Success",
                        ["WO Number"] = "J-CPS-000001310",
                        ["Work order GUID"] = "{11111111-1111-1111-1111-111111111111}"
                    }
                }
            });

            return Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, body));
        });

        var http = new HttpClient(handler);

        var client = BuildClient(http, new FscmOptions
        {
            BaseUrl = "https://fscm.example",
            WoPayloadValidationPath = "" // not configured
        });

        var ctx = new RunContext("run", DateTimeOffset.UtcNow, "Post", "corr");

        var res = await client.ValidateAsync(ctx, JournalType.Expense, normalizedWoPayloadJson: "{}", CancellationToken.None);

        res.IsSuccessStatusCode.Should().BeTrue();
        calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_posts_to_configured_path()
    {
        var calls = new List<string>();

        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            calls.Add(req.RequestUri!.ToString());
            // The client parses the FSCM contract: { "WO Headers": [ { "Status": "Success" ... } ] }
            // Return a minimal valid success payload.
                        var body = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["WO Headers"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["Status"] = "Success",
                        ["WO Number"] = "J-CPS-000001310",
                        ["Work order GUID"] = "{11111111-1111-1111-1111-111111111111}"
                    }
                }
            });

            return Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, body));
        });

        var http = new HttpClient(handler);

        var client = BuildClient(http, new FscmOptions
        {
            BaseUrl = "https://fscm.example",
            WoPayloadValidationPath = "/api/wo/validate"
        });

        var ctx = new RunContext("run", DateTimeOffset.UtcNow, "Post", "corr");

        var res = await client.ValidateAsync(ctx, JournalType.Expense, normalizedWoPayloadJson: @"{""_request"":{""WOList"":[]}}", CancellationToken.None);

        res.IsSuccessStatusCode.Should().BeTrue();
        calls.Should().ContainSingle().Which.Should().Be("https://fscm.example/api/wo/validate");
    }

    private static Rpc.AIS.Accrual.Orchestrator.Core.Abstractions.IFscmWoPayloadValidationClient BuildClient(HttpClient http, FscmOptions opt)
    {
        var reqFactory = new FscmPostRequestFactory();
        var exec = new PassThroughResilientHttpExecutor();
        var aisLogger = new NoopAisLogger();

        return new FscmWoPayloadValidationClient(
            http,
            opt,
            reqFactory,
            exec,
            aisLogger,
            new FakeAisDiagnosticsOptions(),
            NullLogger<FscmWoPayloadValidationClient>.Instance);
    }
}
