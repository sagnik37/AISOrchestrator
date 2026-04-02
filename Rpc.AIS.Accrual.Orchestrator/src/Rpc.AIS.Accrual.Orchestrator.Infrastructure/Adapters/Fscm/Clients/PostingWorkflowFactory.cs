using System;
using System.Collections.Generic;
using System.Net.Http;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Resilience;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

/// <summary>
/// Builds a posting workflow with cohesive collaborators while keeping the typed HttpClient instance.
/// </summary>
public sealed class PostingWorkflowFactory : IPostingWorkflowFactory
{
    private readonly FscmOptions _endpoints;
    private readonly IFscmPostRequestFactory _reqFactory;
    private readonly IResilientHttpExecutor _executor;
    private readonly IWoPayloadValidationEngine _validationEngine;
    private readonly IInvalidPayloadNotifier _invalidPayloadNotifier;
    private readonly IEnumerable<IPostResultHandler> _postResultHandlers;
    private readonly IWoPayloadNormalizer _normalizer;
    private readonly IWoPayloadShapeGuard _shapeGuard;
    private readonly IWoJournalProjector _projector;
    private readonly IFscmPostingResponseParser _responseParser;
    private readonly IPostErrorAggregator _errorAggregator;
    private readonly PayloadPostingDateAdjuster _dateAdjuster;
    private readonly IFsaLineFetcher? _fsaLineFetcher;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IFscmLegalEntityIntegrationParametersClient _leParams;
    private readonly IAisLogger _aisLogger;
    private readonly IAisDiagnosticsOptions _diag;

    public PostingWorkflowFactory(
        FscmOptions endpoints,
        IFscmPostRequestFactory reqFactory,
        IResilientHttpExecutor executor,
        IWoPayloadValidationEngine validationEngine,
        IInvalidPayloadNotifier invalidPayloadNotifier,
        IEnumerable<IPostResultHandler> postResultHandlers,
        IWoPayloadNormalizer normalizer,
        IWoPayloadShapeGuard shapeGuard,
        IWoJournalProjector projector,
        IFscmPostingResponseParser responseParser,
        IPostErrorAggregator errorAggregator,
        PayloadPostingDateAdjuster dateAdjuster,
        IAisLogger aisLogger,
        IAisDiagnosticsOptions diag,
        IFsaLineFetcher? fsaLineFetcher,
        IFscmLegalEntityIntegrationParametersClient leParams,
        ILoggerFactory loggerFactory)
    {
        _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
        _reqFactory = reqFactory ?? throw new ArgumentNullException(nameof(reqFactory));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _validationEngine = validationEngine ?? throw new ArgumentNullException(nameof(validationEngine));
        _invalidPayloadNotifier = invalidPayloadNotifier ?? throw new ArgumentNullException(nameof(invalidPayloadNotifier));
        _postResultHandlers = postResultHandlers ?? Array.Empty<IPostResultHandler>();
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _shapeGuard = shapeGuard ?? throw new ArgumentNullException(nameof(shapeGuard));
        _projector = projector ?? throw new ArgumentNullException(nameof(projector));
        _responseParser = responseParser ?? throw new ArgumentNullException(nameof(responseParser));
        _errorAggregator = errorAggregator ?? throw new ArgumentNullException(nameof(errorAggregator));
        _dateAdjuster = dateAdjuster ?? throw new ArgumentNullException(nameof(dateAdjuster));
        _aisLogger = aisLogger ?? throw new ArgumentNullException(nameof(aisLogger));
        _diag = diag ?? throw new ArgumentNullException(nameof(diag));
        _fsaLineFetcher = fsaLineFetcher;
        _leParams = leParams ?? throw new ArgumentNullException(nameof(leParams));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public IPostingClient Create(HttpClient httpClient)
    {
        if (httpClient is null) throw new ArgumentNullException(nameof(httpClient));

        var poster = new FscmJournalPoster(
            httpClient,
            _endpoints,
            _reqFactory,
            _executor,
            _dateAdjuster,
            _aisLogger,
            _diag,
            _loggerFactory.CreateLogger<FscmJournalPoster>());

        // : type as Core abstraction to match Core pipeline constructor
        Rpc.AIS.Accrual.Orchestrator.Core.Abstractions.IFscmWoPayloadValidationClient fscmValidator =
            new FscmWoPayloadValidationClient(
                httpClient,
                _endpoints,
                _reqFactory,
                _executor,
                _aisLogger,
                _diag,
                _loggerFactory.CreateLogger<FscmWoPayloadValidationClient>());

        var prep = new WoPostingPreparationPipeline(
            _endpoints,
            _normalizer,
            _shapeGuard,
            _validationEngine,
            fscmValidator,
            _invalidPayloadNotifier,
            _projector,
            _dateAdjuster,
            _fsaLineFetcher,
            _leParams,
            _loggerFactory.CreateLogger<WoPostingPreparationPipeline>());

        var outcome = new PostOutcomeProcessor(
            poster,
            _responseParser,
            _errorAggregator,
            _postResultHandlers,
            _loggerFactory.CreateLogger<PostOutcomeProcessor>());

        return new PostingHttpClientWorkflow(
            prep,
            outcome,
            _loggerFactory.CreateLogger<PostingHttpClientWorkflow>());
    }
}
