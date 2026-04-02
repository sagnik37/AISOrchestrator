using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Validation;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;

public sealed partial class WoPostingPreparationPipeline : IWoPostingPreparationPipeline
{
    private readonly FscmOptions _fscm;
    private readonly IFsaLineFetcher? _fsaLineFetcher;
    private readonly IWoPayloadNormalizer _normalizer;
    private readonly IWoPayloadShapeGuard _shapeGuard;
    private readonly IWoPayloadValidationEngine _validationEngine;
    private readonly Rpc.AIS.Accrual.Orchestrator.Core.Abstractions.IFscmWoPayloadValidationClient _fscmValidator;
    private readonly IInvalidPayloadNotifier _invalidPayloadNotifier;
    private readonly IWoJournalProjector _projector;
    private readonly PayloadPostingDateAdjuster _dateAdjuster;
    private readonly ILogger<WoPostingPreparationPipeline> _logger;
    private readonly IFscmLegalEntityIntegrationParametersClient _leParams;


    public WoPostingPreparationPipeline(
        FscmOptions fscm,
        IWoPayloadNormalizer normalizer,
        IWoPayloadShapeGuard shapeGuard,
        IWoPayloadValidationEngine validationEngine,
        Rpc.AIS.Accrual.Orchestrator.Core.Abstractions.IFscmWoPayloadValidationClient fscmValidator,
        IInvalidPayloadNotifier invalidPayloadNotifier,
        IWoJournalProjector projector,
        PayloadPostingDateAdjuster dateAdjuster,
        IFsaLineFetcher? fsaLineFetcher,
         IFscmLegalEntityIntegrationParametersClient leParams,
        ILogger<WoPostingPreparationPipeline> logger)
    {
        _fscm = fscm;
        _normalizer = normalizer;
        _shapeGuard = shapeGuard;
        _validationEngine = validationEngine;
        _fscmValidator = fscmValidator;
        _invalidPayloadNotifier = invalidPayloadNotifier;
        _projector = projector;
        _dateAdjuster = dateAdjuster;
        _leParams = leParams;
        _logger = logger;
        _fsaLineFetcher = fsaLineFetcher;
    }

    public Task<PreparedWoPosting> PrepareValidatedAsync(
        RunContext ctx,
        JournalType journalType,
        string woPayloadJson,
        string? validationResponseRaw,
        CancellationToken ct)
    {
        // Simply delegate to PrepareAsync (keeps compatibility with  interface)
        return PrepareAsync(ctx, journalType, woPayloadJson, ct);
    }

    public async Task<PreparedWoPosting> PrepareAsync(
        RunContext ctx,
        JournalType journalType,
        string woPayloadJson,
        CancellationToken ct)
    {
        var normalized = _normalizer.NormalizeToWoListKey(woPayloadJson);
        _shapeGuard.EnsureValidShapeOrThrow(normalized);

        // IMPORTANT:
        // All downstream work (validation, projection, date adjustment) operates on the normalized payload.
        // Therefore journal name injection must be applied to the normalized JSON, not the original raw input.
        normalized = await InjectJournalNamesIfMissingAsync(ctx, journalType, normalized, ct);

        if (_fsaLineFetcher is not null)
            normalized = await EnrichMissingOperationsDatesFromFsaAsync(ctx, normalized, ct);

        var local = await _validationEngine.ValidateAndFilterAsync(ctx, journalType, normalized, ct);

        if (local.Failures.Count > 0)
        {
            await _invalidPayloadNotifier.NotifyAsync(
                ctx,
                journalType,
                local.Failures,
                local.WorkOrdersBefore,
                local.WorkOrdersAfter,
                ct);
        }

        var filtered = local.FilteredPayloadJson;

        var proj = _projector.Project(filtered, journalType);

        var adjustedProjected =
            await _dateAdjuster.AdjustAsync(ctx, proj.PayloadJson, ct);

        proj = proj with { PayloadJson = adjustedProjected };

        var (retryWo, retryLines) = CountRetryable(local.RetryableFailures);

        return new PreparedWoPosting(
            journalType,
            normalized,
            proj.PayloadJson,
            proj.WorkOrdersBefore,
            proj.WorkOrdersAfter,
            proj.RemovedDueToMissingOrEmptySection,
            ToPostErrors(local.Failures),
            null,
            retryWo,
            retryLines,
            local.RetryablePayloadJson);
    }
}
