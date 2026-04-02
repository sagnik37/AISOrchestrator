// File: Rpc.AIS.Accrual.Orchestrator/src/Rpc.AIS.Accrual.Orchestrator.Core/Services/WoDeltaPayloadService.cs


using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata; // DefaultJsonTypeInfoResolver (net8 reflection-disabled)
using System.Threading;
using System.Threading.Tasks;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;
using Rpc.AIS.Accrual.Orchestrator.Core.Utilities;

namespace Rpc.AIS.Accrual.Orchestrator.Core.Services;

/// <summary>
/// Provides wo delta payload service behavior.
/// </summary>
public sealed partial class WoDeltaPayloadService : IWoDeltaPayloadService, IWoDeltaPayloadServiceV2
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    /// <summary>
    /// Provides keys behavior.
    /// </summary>
    private static class Keys
    {
        public const string Request = "_request";
        public const string WoList = "WOList";

        public const string WorkOrderGuid = "WorkOrderGUID";
        public const string WorkOrderID = "WorkOrderID";
        public const string Company = "Company";
        public const string SubProjectId = "SubProjectId";

        public const string WoItemLines = "WOItemLines";
        public const string WoExpLines = "WOExpLines";
        public const string WoHourLines = "WOHourLines";

        public const string LineType = "LineType";
        public const string JournalLines = "JournalLines";

        public const string WorkOrderLineGuid = "WorkOrderLineGuid";

        public const string Quantity = "Quantity";
        public const string Duration = "Duration";


        public const string UnitCost = "UnitCost";
        public const string UnitAmount = "UnitAmount";

        public const string LineProperty = "LineProperty";
        public const string Warehouse = "Warehouse";
        public const string DimDepartment = "DimensionDepartment";
        public const string DimProduct = "DimensionProduct";

        public const string TransactionDate = "TransactionDate";

        // Do not emit these in DELTA payload
        public const string ModifiedOn = "modifiedon";
        public const string RpcAccrualFieldModifiedOn = "rpc_accrualfieldmodifiedon";

        // Remove from payload (hygiene)
        public const string LineNum = "Line num";
    }

    private readonly IFscmJournalFetchClient _fscmFetch;
    private readonly IFscmAccountingPeriodClient _periodClient;
    private readonly FscmJournalAggregator _aggregator;
    private readonly DeltaCalculationEngine _deltaEngine;
    private readonly IAisLogger _aisLogger;
    private readonly IAisDiagnosticsOptions _diag;


    private readonly WoDeltaPayloadTelemetryShaper _telemetry;

    private readonly DeltaJournalSectionBuilder _sectionBuilder;
    private readonly IFscmLegalEntityIntegrationParametersClient _leParams;

    public WoDeltaPayloadService(
        IFscmJournalFetchClient fscmFetch,
        IFscmAccountingPeriodClient periodClient,
        FscmJournalAggregator aggregator,
        DeltaCalculationEngine deltaEngine,
        IAisLogger aisLogger,
        IAisDiagnosticsOptions diagOptions)
    {
        _fscmFetch = fscmFetch ?? throw new ArgumentNullException(nameof(fscmFetch));
        _periodClient = periodClient ?? throw new ArgumentNullException(nameof(periodClient));
        _aggregator = aggregator ?? throw new ArgumentNullException(nameof(aggregator));
        _deltaEngine = deltaEngine ?? throw new ArgumentNullException(nameof(deltaEngine));
        _aisLogger = aisLogger ?? throw new ArgumentNullException(nameof(aisLogger));
        _diag = diagOptions ?? throw new ArgumentNullException(nameof(diagOptions));
        _telemetry = new WoDeltaPayloadTelemetryShaper(_aisLogger, _diag);
        _sectionBuilder = new DeltaJournalSectionBuilder(_deltaEngine, _aisLogger);
    }

    public Task<WoDeltaPayloadBuildResult> BuildDeltaPayloadAsync(
        RunContext context,
        string fsaWoPayloadJson,
        DateTime todayUtc,
        CancellationToken ct)
        => BuildDeltaPayloadAsync(context, fsaWoPayloadJson, todayUtc, new WoDeltaBuildOptions(null, WoDeltaTargetMode.Normal), ct);

    public Task<WoDeltaPayloadBuildResult> BuildDeltaPayloadAsync(
        RunContext context,
        string fsaWoPayloadJson,
        DateTime todayUtc,
        WoDeltaBuildOptions options,
        CancellationToken ct)
        => BuildDeltaPayloadInternalAsync(context, fsaWoPayloadJson, todayUtc, options ?? new WoDeltaBuildOptions(null, WoDeltaTargetMode.Normal), ct);
}
