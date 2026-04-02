using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.InvoiceAttributes;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Tests.TestDoubles;

using Xunit;

namespace Rpc.AIS.Accrual.Orchestrator.Tests.Integration;

public sealed class FscmInvoiceAttributesHttpClientContractTests
{
    [Fact]
    public async Task UpdateAsync_posts_expected_envelope_to_configured_path()
    {
        var calls = new List<(string Url, string Body)>();

        var handler = new FakeHttpMessageHandler(async (req, ct) =>
        {
            var body = req.Content is null ? "" : await req.Content.ReadAsStringAsync(ct);
            calls.Add((req.RequestUri!.ToString(), body));
            return FakeHttpMessageHandler.Json(HttpStatusCode.OK, @"{""ok"":true}");
        });

        var http = new HttpClient(handler);

        var options = Options.Create(new FscmOptions
        {
            BaseUrl = "https://fscm.example",
            UpdateInvoiceAttributesPath = "/api/invattrs/update"
        });

        var payloadBuilder = new InvoiceAttributesPayloadBuilder();

        var aisLogger = new FakeAisLogger();
        var diagnosticsOptions = new FakeAisDiagnosticsOptions();

        var transport = new InvoiceAttributesHttpTransport(
            aisLogger,
            diagnosticsOptions,
            NullLogger<InvoiceAttributesHttpTransport>.Instance);

        var parser = new InvoiceAttributesResponseParser(
            NullLogger<InvoiceAttributesResponseParser>.Instance);

        var client = new FscmInvoiceAttributesHttpClient(
            http,
            options,
            payloadBuilder,
            transport,
            parser,
            NullLogger<FscmInvoiceAttributesHttpClient>.Instance);

        var ctx = new RunContext(
            RunId: "run",
            StartedAtUtc: DateTimeOffset.UtcNow,
            TriggeredBy: "Post",
            CorrelationId: "corr");

        var updates = new List<InvoiceAttributePair>
        {
            new("WellName", "Alpha-1"),
            new("WellNumber", "123")
        };

        var result = await client.UpdateAsync(
            ctx,
            company: "425",
            subProjectId: "425-P0000100-00011",
            workOrderGuid: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            workOrderId: "J-CPS-000001310",
            countryRegionId: "USA",
            county: "OH",
            state: "OH",
            dimensionDisplayValue: "-0375-119-----",
            fsaTaxabilityType: "TX",
            fsaWellAge: "New",
            fsaWorkType: "WT",
            additionalHeaderFields: null,
            updates: updates,
            ct: CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        calls.Should().HaveCount(1);
        calls[0].Url.Should().Be("https://fscm.example/api/invattrs/update");

        using var doc = JsonDocument.Parse(calls[0].Body);
        doc.RootElement.TryGetProperty("_request", out var reqEl).Should().BeTrue();
        reqEl.TryGetProperty("WOList", out var woList).Should().BeTrue();
        woList.ValueKind.Should().Be(JsonValueKind.Array);
        woList.GetArrayLength().Should().Be(1);

        var wo = woList[0];
        wo.GetProperty("Company").GetString().Should().Be("425");
        wo.GetProperty("SubProjectId").GetString().Should().Be("425-P0000100-00011");
        wo.GetProperty("WorkOrderGUID").GetString().Should().Be("{11111111-1111-1111-1111-111111111111}".ToUpperInvariant());
        wo.GetProperty("WorkOrderID").GetString().Should().Be("J-CPS-000001310");

        var attrs = wo.GetProperty("InvoiceAttributes");
        attrs.ValueKind.Should().Be(JsonValueKind.Array);
        attrs.GetArrayLength().Should().Be(2);
        attrs[0].GetProperty("AttributeName").GetString().Should().Be("WellName");
        attrs[0].GetProperty("AttributeValue").GetString().Should().Be("Alpha-1");
    }

    private sealed class FakeAisLogger : IAisLogger
    {
        public Task InfoAsync(
            string operationName,
            string message,
            string source,
            object? payload = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task WarnAsync(
            string operationName,
            string message,
            string source,
            object? payload = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ErrorAsync(
            string operationName,
            string message,
            string source,
            Exception? exception = null,
            object? payload = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task LogPayloadAsync(
            string operationName,
            string payloadName,
            string payload,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task LogJsonPayloadAsync(
            string operationName,
            string payloadName,
            string payload,
            CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeAisDiagnosticsOptions : IAisDiagnosticsOptions
    {
        public bool LogPayloadBodies => false;
        public bool LogMultiWoPayloadBody => false;
        public bool IncludeDeltaReasonKey => false;
        public int PayloadSnippetChars => 0;
        public int PayloadChunkChars => 0;
    }
}
