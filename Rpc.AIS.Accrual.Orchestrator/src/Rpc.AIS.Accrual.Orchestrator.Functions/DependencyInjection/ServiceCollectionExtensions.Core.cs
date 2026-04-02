using System;

using Azure.Communication.Email;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Rpc.AIS.Accrual.Orchestrator.Application.Features.Delta.FsaDeltaPayload.Services.EnrichmentPipeline;
using Rpc.AIS.Accrual.Orchestrator.Application.Features.Delta.FsaDeltaPayload.Services.EnrichmentPipeline.Steps;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;
using Rpc.AIS.Accrual.Orchestrator.Core.Options;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.FsaDeltaPayload.Mappers;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.InvoiceAttributes;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.JournalPolicies;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.WoPayloadValidationPipeline;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.WoPayloadValidationRules;
using Rpc.AIS.Accrual.Orchestrator.Core.UseCases.FsaDeltaPayload;
using Rpc.AIS.Accrual.Orchestrator.Functions.Functions;
using Rpc.AIS.Accrual.Orchestrator.Functions.Services;
using Rpc.AIS.Accrual.Orchestrator.Functions.Services.Handlers;
using Rpc.AIS.Accrual.Orchestrator.Functions.Services.Summaries;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Notifications;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Resilience;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Utilities;

using CoreNotificationOptions = Rpc.AIS.Accrual.Orchestrator.Core.Options.NotificationOptions;
using InfraNotificationOptions = Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options.NotificationOptions;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.DependencyInjection;

internal static partial class ServiceCollectionExtensions
{
    internal static IServiceCollection AddCoreServices(this IServiceCollection services, IConfiguration cfg)
    {
        services.RegisterOptions(cfg);
        services.RegisterCoreDomainServices();
        services.RegisterCrossCuttingServices();
        services.RegisterFunctionLayerServices();
        return services;
    }

    internal static IServiceCollection AddEnrichmentPipeline(this IServiceCollection services)
    {
        services.AddSingleton<IFsaDeltaPayloadEnrichmentPipeline, DefaultFsaDeltaPayloadEnrichmentPipeline>();

        services.AddSingleton<IFsaDeltaPayloadEnrichmentStep, FsExtrasEnrichmentStep>();
        services.AddSingleton<IFsaDeltaPayloadEnrichmentStep, CompanyEnrichmentStep>();
        services.AddSingleton<IFsaDeltaPayloadEnrichmentStep, JournalNamesEnrichmentStep>();
        services.AddSingleton<IFsaDeltaPayloadEnrichmentStep, SubProjectEnrichmentStep>();
        services.AddSingleton<IFsaDeltaPayloadEnrichmentStep, WorkOrderHeaderFieldsEnrichmentStep>();
        services.AddSingleton<IFsaDeltaPayloadEnrichmentStep, JournalDescriptionsEnrichmentStep>();
        return services;
    }

    private static void RegisterOptions(this IServiceCollection services, IConfiguration cfg)
    {
        services.AddOptions<ProcessingOptions>()
            .Bind(cfg.GetSection(ProcessingOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<CoreNotificationOptions>()
            .Bind(cfg.GetSection(CoreNotificationOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<AisDiagnosticsOptions>()
            .Bind(cfg.GetSection("AisLogging"))
            .ValidateOnStart();

        services.AddOptions<InfraNotificationOptions>()
            .Bind(cfg.GetSection(InfraNotificationOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<AcsEmailOptions>()
            .Bind(cfg.GetSection(AcsEmailOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<WarmupOptions>()
            .Bind(cfg.GetSection(WarmupOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<FscmODataStagingOptions>()
            .Bind(cfg.GetSection(FscmODataStagingOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<HttpResilienceOptions>()
            .Configure<IConfiguration>((opt, c) =>
            {
                c.GetSection(HttpResilienceOptions.SectionName).Bind(opt);
                c.GetSection("HttpResilience").Bind(opt);
            })
            .ValidateOnStart();

        services.AddOptions<FsOptions>()
            .Configure<IConfiguration>((o, c) =>
            {
                o.DataverseApiBaseUrl = c["FsaIngestion:DataverseApiBaseUrl"] ?? string.Empty;
                o.WorkOrderFilter = c["FsaIngestion:WorkOrderFilter"];

                if (int.TryParse(c["FsaIngestion:PageSize"], out var ps)) o.PageSize = ps;
                if (int.TryParse(c["FsaIngestion:MaxPages"], out var mp)) o.MaxPages = mp;
                if (int.TryParse(c["FsaIngestion:PreferMaxPageSize"], out var pmp)) o.PreferMaxPageSize = pmp;
                if (int.TryParse(c["FsaIngestion:OrFilterChunkSize"], out var cs)) o.OrFilterChunkSize = cs;

                o.TenantId = c["Dataverse:Auth:TenantId"] ?? string.Empty;
                o.ClientId = c["Dataverse:Auth:ClientId"] ?? string.Empty;
                o.ClientSecret = c["Dataverse:Auth:ClientSecret"] ?? string.Empty;

                if (bool.TryParse(c["Fs:SyncInternalDocumentsToFscm"], out var syncInternalDocs) ||
                    bool.TryParse(c["Sync internal documents to FSCM"], out syncInternalDocs))
                {
                    o.SyncInternalDocumentsToFscm = syncInternalDocs;
                }
            })
            .Validate(o =>
                    !string.IsNullOrWhiteSpace(o.DataverseApiBaseUrl) &&
                    !string.IsNullOrWhiteSpace(o.TenantId) &&
                    !string.IsNullOrWhiteSpace(o.ClientId) &&
                    !string.IsNullOrWhiteSpace(o.ClientSecret),
                "Missing required FS config. Required: FsaIngestion:DataverseApiBaseUrl and Dataverse:Auth:*")
            .ValidateOnStart();

        services.AddOptions<FscmOptions>()
            .Configure<IConfiguration>((o, c) =>
            {
                o.BaseUrl = c["Endpoints:FscmBaseUrl"] ?? string.Empty;
                o.TenantId = c["Fscm:Auth:TenantId"] ?? string.Empty;
                o.ClientId = c["Fscm:Auth:ClientId"] ?? string.Empty;
                o.ClientSecret = c["Fscm:Auth:ClientSecret"] ?? string.Empty;
                o.DefaultScope = c["Fscm:Auth:DefaultScope"] ?? string.Empty;
                o.SubProjectPath = c["Fscm:SubProjectPath"] ?? string.Empty;
                o.JournalValidatePath = c["Fscm:JournalValidatePath"] ?? string.Empty;
                o.JournalCreatePath = c["Fscm:JournalCreatePath"] ?? string.Empty;
                o.JournalPostPath = c["Fscm:JournalPostPath"] ?? string.Empty;
                o.JournalPostCustomPath = o.JournalPostPath;
                o.UpdateInvoiceAttributesPath = c["Fscm:UpdateInvoiceAttributesPath"] ?? string.Empty;
                o.UpdateProjectStatusPath = c["Fscm:UpdateProjectStatusPath"] ?? string.Empty;

                var docPath = c["Fscm:DocuAttachmentsPath"];
                if (!string.IsNullOrWhiteSpace(docPath))
                    o.DocuAttachmentsPath = docPath;

                if (int.TryParse(c["Fscm:JournalHistoryOrFilterChunkSize"], out var jcs) && jcs > 0)
                    o.JournalHistoryOrFilterChunkSize = Math.Min(jcs, 200);

                if (int.TryParse(c["Fscm:ReleasedDistinctProductsOrFilterChunkSize"], out var rcs) && rcs > 0)
                    o.ReleasedDistinctProductsOrFilterChunkSize = Math.Min(rcs, 200);
            })
            .Validate(o =>
                    !string.IsNullOrWhiteSpace(o.TenantId) &&
                    !string.IsNullOrWhiteSpace(o.ClientId) &&
                    !string.IsNullOrWhiteSpace(o.ClientSecret),
                "Missing required FSCM auth config under Fscm:Auth:*")
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<FscmOptions>, FscmOptionsStartupValidator>();

        services.AddSingleton(sp => sp.GetRequiredService<IOptions<ProcessingOptions>>().Value);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<AisDiagnosticsOptions>>().Value);
        services.AddSingleton<IAisDiagnosticsOptions>(sp => sp.GetRequiredService<AisDiagnosticsOptionsAdapter>());
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<CoreNotificationOptions>>().Value);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<InfraNotificationOptions>>().Value);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<FsOptions>>().Value);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<FscmOptions>>().Value);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<FscmODataStagingOptions>>().Value);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<AcsEmailOptions>>().Value);
    }

    private static void RegisterCoreDomainServices(this IServiceCollection services)
    {
        services.AddSingleton<IRunIdGenerator, RunIdGenerator>();
        services.AddSingleton<JournalReversalPlanner>();
        services.AddSingleton<DeltaCalculationEngine>();
        services.AddSingleton<FscmJournalAggregator>();
        services.AddSingleton<DeltaComparer>();

        services.AddSingleton<IWoDeltaPayloadService, WoDeltaPayloadService>();
        services.AddSingleton<IWoDeltaPayloadServiceV2, WoDeltaPayloadService>();
        services.AddSingleton<SubProjectProvisioningService>();

        services.AddSingleton<InvoiceAttributeDeltaBuilder>();
        services.AddSingleton<RuntimeInvoiceAttributeMapper>();

        services.AddSingleton<IFsaProductLineMapper, FsaProductLineMapper>();
        services.AddSingleton<IFsaServiceLineMapper, FsaServiceLineMapper>();
        services.AddSingleton<IFsaSnapshotBuilder, FsaSnapshotBuilder>();

        services.AddSingleton<IFsaDeltaPayloadUseCase, FsaDeltaPayloadUseCase>();
        services.AddSingleton<IFsaDeltaPayloadEnricher, FsaDeltaPayloadEnricher>();

        services.AddSingleton<IJournalTypePolicy, ItemJournalTypePolicy>();
        services.AddSingleton<IJournalTypePolicy, ExpenseJournalTypePolicy>();
        services.AddSingleton<IJournalTypePolicy, HourJournalTypePolicy>();
        services.AddSingleton<IJournalTypePolicyResolver, JournalTypePolicyResolver>();

        services.AddSingleton<IFscmReferenceValidator, FscmReferenceValidator>();
        services.AddSingleton<IWoPayloadValidationEngine, WoPayloadValidationPipelineEngine>();
        services.AddSingleton<IWoPayloadRule, WoEnvelopeParseRule>();
        services.AddSingleton<IWoPayloadRule, WoLocalValidationRule>();
        services.AddSingleton<IWoPayloadRule, WoFscmCustomValidationRule>();
        services.AddSingleton<IWoPayloadRule, WoBuildResultRule>();
        services.AddSingleton<IWoEnvelopeParser, WoEnvelopeParser>();
        services.AddSingleton<IWoLocalValidator, WoLocalValidator>();
        services.AddSingleton<IWoValidationResultBuilder, WoValidationResultBuilder>();
    }

    private static void RegisterCrossCuttingServices(this IServiceCollection services)
    {
        services.AddSingleton<IAisLogger, AppInsightsAisLogger>();
        services.AddSingleton<AisDiagnosticsOptionsAdapter>();
        services.AddSingleton<ITelemetry, Telemetry>();
        services.AddSingleton<IProcessingAuditService, ProcessingAuditService>();

        services.AddSingleton<IResilientHttpExecutor, ResilientHttpExecutor>();
        services.AddSingleton<IHttpFailureClassifier, DefaultHttpFailureClassifier>();

        services.AddSingleton(sp =>
        {
            var opt = sp.GetRequiredService<IOptions<AcsEmailOptions>>().Value;
            return new EmailClient(opt.ConnectionString);
        });

        services.AddSingleton<IEmailSender, AcsEmailSender>();
        services.AddSingleton<IInvalidPayloadNotifier, InvalidPayloadEmailNotifier>();
    }

    private static void RegisterFunctionLayerServices(this IServiceCollection services)
    {
        services.AddSingleton<IFsaDeltaPayloadOrchestrator, FsaDeltaPayloadOrchestrator>();
        services.AddSingleton<IJobOperationsHttpUseCase, JobOperationsHttpHandlerCommon>();
        services.AddSingleton<JobOperationsHttpHandlerCommon>();

        services.AddSingleton<IAdHocSingleJobUseCase, AdHocSingleJobUseCase>();
        services.AddSingleton<IAdHocAllJobsUseCase, AdHocAllJobsUseCase>();
        services.AddSingleton<IAdHocAllStatusUseCase, AdHocAllStatusUseCase>();
        services.AddSingleton<IPostJobUseCase, PostJobUseCase>();
        services.AddSingleton<IWarmupReadUseCase, WarmupReadUseCase>();
        services.AddSingleton<ICancelJobUseCase, CancelJobUseCase>();
        services.AddSingleton<ICustomerChangeUseCase, CustomerChangeUseCase>();

        services.AddSingleton<IWoPayloadCandidateExtractor, WoPayloadCandidateExtractor>();
        services.AddSingleton<IWorkOrderProcessingSummaryBuilder, WorkOrderProcessingSummaryBuilder>();
        services.AddSingleton<IRetryableWoPayloadPoster, RetryableWoPayloadPoster>();
        services.AddSingleton<IPostingAuditWriter, PostingAuditWriter>();
        services.AddSingleton<ValidateAndPostWoPayloadHandler>();
        services.AddSingleton<PostSingleWorkOrderHandler>();
        services.AddSingleton<UpdateWorkOrderStatusHandler>();
        services.AddSingleton<PostRetryableWoPayloadHandler>();
        services.AddSingleton<SyncInvoiceAttributesHandler>();
        services.AddSingleton<FinalizeAndNotifyWoPayloadHandler>();
        services.AddSingleton<IActivitiesUseCase, ActivitiesUseCase>();

        services.AddSingleton<ICustomerChangeOrchestrator, CustomerChangeOrchestrator>();
        services.AddSingleton<IFscmProjectLifecycle, NoopFscmProjectLifecycle>();
        services.AddSingleton<InvoiceAttributeSyncRunner>();
        services.AddSingleton<InvoiceAttributesUpdateRunner>();
    }
}
