using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

using Xunit;

namespace Rpc.AIS.Accrual.Orchestrator.Tests.Infrastructure;

public sealed class FsWorkOrderAttachmentHttpClientTests
{
    [Fact]
    public async Task GetForWorkOrderAsync_returns_external_only_when_internal_sync_disabled()
    {
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent("""
                {
                  "value": [
                    { "rpc_filename": "ext.pdf", "rpc_bloburl": "https://blob/ext.pdf", "rpc_confidentiality": " External " },
                    { "rpc_filename": "int.pdf", "rpc_bloburl": "https://blob/int.pdf", "rpc_confidentiality": "Internal" },
                    { "rpc_filename": "misc.pdf", "rpc_bloburl": "https://blob/misc.pdf", "rpc_confidentiality": "Other" }
                  ]
                }
                """)
            });

        var sut = CreateSut(handler, new FsOptions { PreferMaxPageSize = 100, SyncInternalDocumentsToFscm = false });

        var result = await sut.GetForWorkOrderAsync(NewRunContext(), Guid.Parse("11111111-1111-1111-1111-111111111111"), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].FileName.Should().Be("ext.pdf");
    }

    [Fact]
    public async Task GetForWorkOrderAsync_returns_external_and_internal_when_internal_sync_enabled()
    {
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent("""
                {
                  "value": [
                    { "rpc_filename": "ext.pdf", "rpc_bloburl": "https://blob/ext.pdf", "rpc_confidentiality": "External" },
                    { "rpc_filename": "int.pdf", "rpc_bloburl": "https://blob/int.pdf", "rpc_confidentiality": " Internal " }
                  ]
                }
                """)
            });

        var sut = CreateSut(handler, new FsOptions { PreferMaxPageSize = 100, SyncInternalDocumentsToFscm = true });

        var result = await sut.GetForWorkOrderAsync(NewRunContext(), Guid.Parse("11111111-1111-1111-1111-111111111111"), CancellationToken.None);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetForWorkOrderAsync_returns_all_when_confidentiality_field_missing()
    {
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent("{\"error\":{\"message\":\"Could not find a property named 'rpc_confidentiality'\"}}")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent("""
                {
                  "value": [
                    { "rpc_filename": "doc1.pdf", "rpc_bloburl": "https://blob/doc1.pdf" },
                    { "rpc_filename": "doc2.pdf", "rpc_bloburl": "https://blob/doc2.pdf" }
                  ]
                }
                """)
            });

        var sut = CreateSut(handler, new FsOptions { PreferMaxPageSize = 100, SyncInternalDocumentsToFscm = false });

        var result = await sut.GetForWorkOrderAsync(NewRunContext(), Guid.Parse("11111111-1111-1111-1111-111111111111"), CancellationToken.None);

        result.Should().HaveCount(2);
    }

    private static FsWorkOrderAttachmentHttpClient CreateSut(HttpMessageHandler handler, FsOptions options)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.crm.dynamics.com/api/data/v9.2/")
        };

        return new FsWorkOrderAttachmentHttpClient(
            http,
            Options.Create(options),
            NullLogger<FsWorkOrderAttachmentHttpClient>.Instance);
    }

    private static RunContext NewRunContext()
        => new("run-1", DateTimeOffset.UtcNow, "Test", "corr-1", "UnitTest", "001");

    private static StringContent JsonContent(string json)
        => new(json, Encoding.UTF8, "application/json");

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public SequenceHandler(params HttpResponseMessage[] responses)
            => _responses = new Queue<HttpResponseMessage>(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
                throw new InvalidOperationException($"No queued response available for request: {request.RequestUri}");

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
