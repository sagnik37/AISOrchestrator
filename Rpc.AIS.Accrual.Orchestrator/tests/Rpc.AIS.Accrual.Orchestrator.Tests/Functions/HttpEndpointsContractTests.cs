using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;
using Rpc.AIS.Accrual.Orchestrator.Functions.Functions;
using Rpc.AIS.Accrual.Orchestrator.Functions.Services;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Tests.TestDoubles;

using Xunit;

namespace Rpc.AIS.Accrual.Orchestrator.Tests.Functions;

public sealed class HttpEndpointsContractTests
{
    [Fact]
    public async Task AdHocSingle_empty_body_returns_400()
    {
        var useCase = BuildAdHocSingle(out _);

        var ctx = new TestFunctionContext();
        var req = NewReq(ctx, body: "");

        var res = await useCase.ExecuteAsync(req, ctx);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AdHocSingle_valid_envelope_passes_triggeredBy_to_payload_orchestrator()
    {
        var useCase = BuildAdHocSingle(out var payloadOrch);

        GetFsaDeltaPayloadInputDto? captured = null;
        payloadOrch
            .Setup(x => x.BuildSingleWorkOrderAnyStatusAsync(
                It.IsAny<GetFsaDeltaPayloadInputDto>(),
                It.IsAny<FsOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<GetFsaDeltaPayloadInputDto, FsOptions, CancellationToken>((dto, _, __) => captured = dto)
            // Force early NotFound response (so we don't execute delta/post/update).
            .ReturnsAsync(new GetFsaDeltaPayloadResultDto(PayloadJson: "", ProductDeltaLinkAfter: null, ServiceDeltaLinkAfter: null, WorkOrderNumbers: Array.Empty<string>()));

        var fctx = new TestFunctionContext();
        var req = NewReq(fctx, body: Envelope(workOrderGuid: "11111111-1111-1111-1111-111111111111"));

        var res = await useCase.ExecuteAsync(req, fctx);
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);

        captured.Should().NotBeNull();
        captured!.TriggeredBy.Should().Be("AdHocSingle");
    }

    [Fact]
    public async Task AdHocAll_schedules_durable_with_triggeredBy_AdHocAll()
    {
        var useCase = new AdHocAllJobsUseCase(
            NullLogger<AdHocAllJobsUseCase>.Instance,
            new NoopAisLogger(),
            new FakeAisDiagnosticsOptions());

        // NOTE:
        // DurableTaskClient is a concrete class without a public parameterless ctor,
        // so Moq cannot proxy it. Use a minimal concrete test double instead.
        var durable = new CapturingDurableTaskClient();

        var fctx = new TestFunctionContext();
        var req = NewReq(fctx, body: "{\"_request\":{\"RunId\":\"adhoc-run-1\",\"CorrelationId\":\"corr-1\"}}");

        var res = await useCase.ExecuteAsync(req, durable, fctx);
        res.StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var acceptedPayload = await ReadJsonAsync(res);
        acceptedPayload.RootElement.GetProperty("Status").GetString().Should().Be("Accepted");
        acceptedPayload.RootElement.GetProperty("Trigger").GetString().Should().Be("AdHocAll");
        acceptedPayload.RootElement.GetProperty("TrackingMode").GetString().Should().Be("LogsAndDurableStatus");
        acceptedPayload.RootElement.GetProperty("RuntimeStatus").GetString().Should().Be("Pending");
        acceptedPayload.RootElement.GetProperty("StatusQueryRoute").GetString().Should().Contain("/api/adhoc/batch/status");
        acceptedPayload.RootElement.GetProperty("OrchestrationInstanceId").GetString().Should().Contain("-adhoc-all");

        durable.CapturedInput.Should().NotBeNull();
        durable.CapturedInput!.GetType().Name.Should().Contain("RunInputDto");

        var triggeredByProp = durable.CapturedInput.GetType().GetProperty("TriggeredBy");
        triggeredByProp.Should().NotBeNull();
        triggeredByProp!.GetValue(durable.CapturedInput)!.ToString().Should().Be("AdHocAll");

        durable.CapturedInstanceId.Should().NotBeNull();
        durable.CapturedInstanceId!.Should().Be("adhoc-run-1-adhoc-all");

        var runIdProp = durable.CapturedInput.GetType().GetProperty("RunId");
        runIdProp.Should().NotBeNull();
        runIdProp!.GetValue(durable.CapturedInput)!.ToString().Should().Be("adhoc-run-1");
    }

    [Fact]
    public async Task CancelJob_empty_payload_requires_company_and_subproject_in_envelope()
    {
        var payloadOrch = new Mock<IFsaDeltaPayloadOrchestrator>(MockBehavior.Strict);
        payloadOrch
            .Setup(x => x.BuildSingleWorkOrderAnyStatusAsync(
                It.IsAny<GetFsaDeltaPayloadInputDto>(),
                It.IsAny<FsOptions>(),
                It.IsAny<CancellationToken>()))
            // Force the empty-payload path
            .ReturnsAsync(new GetFsaDeltaPayloadResultDto(PayloadJson: "", ProductDeltaLinkAfter: null, ServiceDeltaLinkAfter: null, WorkOrderNumbers: Array.Empty<string>()));

        var useCase = new CancelJobUseCase(
            NullLogger<CancelJobUseCase>.Instance,
            new NoopAisLogger(),
            new FakeAisDiagnosticsOptions(),
            payloadOrch.Object,
            new FsOptions(),
            Mock.Of<IPostingClient>(),
            Mock.Of<IWoDeltaPayloadServiceV2>(),
            Mock.Of<IFscmProjectStatusClient>(),
            new InvoiceAttributeSyncRunner(
                NullLogger<InvoiceAttributeSyncRunner>.Instance,
                new Mock<IFsaLineFetcher>(MockBehavior.Loose).Object,
                new Mock<IFscmInvoiceAttributesClient>(MockBehavior.Loose).Object,
                new Mock<IFscmGlobalAttributeMappingClient>(MockBehavior.Loose).Object),
            new InvoiceAttributesUpdateRunner(
                new Mock<IFscmInvoiceAttributesClient>(MockBehavior.Loose).Object,
                NullLogger<InvoiceAttributesUpdateRunner>.Instance),
            new DocumentAttachmentCopyRunner(
                new Mock<IFsWorkOrderAttachmentClient>(MockBehavior.Loose).Object,
                new Mock<IFscmDocuAttachmentsClient>(MockBehavior.Loose).Object,
                NullLogger<DocumentAttachmentCopyRunner>.Instance));

        var fctx = new TestFunctionContext();

        // Missing Company/SubProjectId in envelope => should 400
        var req = NewReq(fctx, body: Envelope(workOrderGuid: "11111111-1111-1111-1111-111111111111"));
        var res = await useCase.ExecuteAsync(req, fctx);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }



    [Fact]
    public async Task AdHocSingle_test_business_event_returns_202_and_skips_processing()
    {
        var useCase = BuildAdHocSingle(out _);

        var ctx = new TestFunctionContext();
        var req = NewReq(ctx, body: TestBusinessEventPayload());

        var res = await useCase.ExecuteAsync(req, ctx);
        res.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task AdHocAll_test_business_event_returns_202_and_skips_scheduling()
    {
        var useCase = new AdHocAllJobsUseCase(
            NullLogger<AdHocAllJobsUseCase>.Instance,
            new NoopAisLogger(),
            new FakeAisDiagnosticsOptions());

        var durable = new CapturingDurableTaskClient();
        var fctx = new TestFunctionContext();
        var req = NewReq(fctx, body: TestBusinessEventPayload());

        var res = await useCase.ExecuteAsync(req, durable, fctx);
        res.StatusCode.Should().Be(HttpStatusCode.Accepted);
        durable.CapturedInput.Should().BeNull();
    }

    private static AdHocSingleJobUseCase BuildAdHocSingle(out Mock<IFsaDeltaPayloadOrchestrator> payloadOrch)
    {
        payloadOrch = new Mock<IFsaDeltaPayloadOrchestrator>(MockBehavior.Strict);

        // The rest are not invoked in our early-exit tests.
        var deltaV2 = new Mock<IWoDeltaPayloadServiceV2>(MockBehavior.Loose);
        var posting = new Mock<IPostingClient>(MockBehavior.Loose);

        // Concrete runners need concrete instances, but we keep them unused by early exit.
        var fsaLineFetcher = new Mock<IFsaLineFetcher>(MockBehavior.Loose);
        var fscmInvAttrs = new Mock<IFscmInvoiceAttributesClient>(MockBehavior.Loose);
        var attrMap = new Mock<IFscmGlobalAttributeMappingClient>(MockBehavior.Loose);

        var invoiceSync = new InvoiceAttributeSyncRunner(
            NullLogger<InvoiceAttributeSyncRunner>.Instance,
            fsaLineFetcher.Object,
            fscmInvAttrs.Object,
            attrMap.Object);

        var invoiceUpdate = new InvoiceAttributesUpdateRunner(
            fscmInvAttrs.Object,
            NullLogger<InvoiceAttributesUpdateRunner>.Instance);

        var docCopy = new DocumentAttachmentCopyRunner(
            new Mock<IFsWorkOrderAttachmentClient>(MockBehavior.Loose).Object,
            new Mock<IFscmDocuAttachmentsClient>(MockBehavior.Loose).Object,
            NullLogger<DocumentAttachmentCopyRunner>.Instance);

        return new AdHocSingleJobUseCase(
            NullLogger<AdHocSingleJobUseCase>.Instance,
            new NoopAisLogger(),
            new FakeAisDiagnosticsOptions(),
            payloadOrch.Object,
            new FsOptions(),
            posting.Object,
            deltaV2.Object,
            invoiceSync,
            invoiceUpdate,
            docCopy);
    }

    private static FakeHttpRequestData NewReq(FunctionContext ctx, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
        return new FakeHttpRequestData(ctx, new Uri("https://example/api"), new MemoryStream(bytes));
    }

    private static string Envelope(string workOrderGuid)
    {
        var obj = new
        {
            _request = new
            {
                WOList = new[]
                {
                new
                {
                    WorkOrderGUID = "{" + workOrderGuid + "}"
                }
            }
            }
        };

        return JsonSerializer.Serialize(obj);
    }


    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseData response)
    {
        response.Body.Position = 0;
        return await JsonDocument.ParseAsync(response.Body);
    }

    private static string TestBusinessEventPayload()
        => JsonSerializer.Serialize(new
        {
            BusinessEventId = "BusinessEventsTestEndpointContract",
            BusinessEventLegalEntity = "001",
            ControlNumber = -9223372036854775808L,
            EventId = "8D1220EB-E514-4E35-844E-E062DEF0C05D"
        });

}
