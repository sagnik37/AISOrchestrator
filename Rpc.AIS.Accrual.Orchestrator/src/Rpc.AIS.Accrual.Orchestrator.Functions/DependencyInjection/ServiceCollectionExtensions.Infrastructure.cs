using System;
using System.Net.Http;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Polly;

using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Services;
using Rpc.AIS.Accrual.Orchestrator.Core.Services.JournalPolicies;
using Rpc.AIS.Accrual.Orchestrator.Functions.Services;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.FscmJournalPolicies;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Http;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Resilience;

using IFscmWoPayloadValidationClient = Rpc.AIS.Accrual.Orchestrator.Core.Abstractions.IFscmWoPayloadValidationClient;
using ResilienceHttpPolicies = global::Rpc.AIS.Accrual.Orchestrator.Infrastructure.Resilience.HttpPolicies;

namespace Rpc.AIS.Accrual.Orchestrator.Functions.DependencyInjection;

internal static partial class ServiceCollectionExtensions
{
    internal static IServiceCollection AddInfrastructureClients(this IServiceCollection services, IConfiguration cfg)
    {
        services.AddSingleton<ResilienceHttpPolicies>();

        services.AddSingleton<IDataverseTokenProvider, DataverseBearerTokenProvider>();
        services.AddSingleton<IFscmTokenProvider, FscmBearerTokenProvider>();
        services.AddTransient<DataverseAuthHandler>();
        services.AddTransient<FscmAuthHandler>();

        services.RegisterWarmupClients();
        services.RegisterDataverseClients();
        services.RegisterFscmPostingClients();
        services.RegisterFscmReferenceClients(cfg);

        return services;
    }

    private static void RegisterWarmupClients(this IServiceCollection services)
    {
        AddDataversePolicies(
            services.AddHttpClient("warmup-dataverse")
                .ConfigureHttpClient((sp, client) =>
                {
                    var o = sp.GetRequiredService<IOptions<FsOptions>>().Value;
                    client.BaseAddress = new Uri(o.DataverseApiBaseUrl, UriKind.Absolute);
                })
                .AddHttpMessageHandler<DataverseAuthHandler>(),
            clientName: "warmup-dataverse");

        AddFscmPolicies(
            services.AddHttpClient("warmup-fscm")
                .ConfigureHttpClient((sp, client) =>
                {
                    var o = sp.GetRequiredService<IOptions<FscmOptions>>().Value;
                    client.BaseAddress = new Uri(o.BaseUrl, UriKind.Absolute);
                })
                .AddHttpMessageHandler<FscmAuthHandler>(),
            clientName: "warmup-fscm");
    }

    private static void RegisterDataverseClients(this IServiceCollection services)
    {
        services.AddSingleton<IFsaODataQueryBuilder, FsaODataQueryBuilder>();
        services.AddSingleton<IODataPagedReader, ODataPagedReader>();
        services.AddSingleton<IFsaRowFlattener, FsaRowFlattener>();
        services.AddSingleton<IInvoiceAttributesPayloadBuilder, InvoiceAttributesPayloadBuilder>();
        services.AddSingleton<IInvoiceAttributesHttpTransport, InvoiceAttributesHttpTransport>();
        services.AddSingleton<IInvoiceAttributesResponseParser, InvoiceAttributesResponseParser>();

        AddDataversePolicies(
            services.AddHttpClient<IWarehouseSiteEnricher, WarehouseSiteEnricher>((sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<FsOptions>>().Value;
                http.BaseAddress = new Uri(opt.DataverseApiBaseUrl!, UriKind.Absolute);
            }).AddHttpMessageHandler<DataverseAuthHandler>(),
            clientName: "dataverse-warehouse-site-enricher");

        AddDataversePolicies(
            services.AddHttpClient<IVirtualLookupNameResolver, VirtualLookupNameResolver>((sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<FsOptions>>().Value;
                http.BaseAddress = new Uri(opt.DataverseApiBaseUrl!, UriKind.Absolute);
            }).AddHttpMessageHandler<DataverseAuthHandler>(),
            clientName: "dataverse-virtual-lookup-name-resolver");

        AddDataversePolicies(
            services.AddHttpClient<FsaLineFetcher>((sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<FsOptions>>().Value;
                http.BaseAddress = new Uri(opt.DataverseApiBaseUrl!, UriKind.Absolute);
            }).AddHttpMessageHandler<DataverseAuthHandler>(),
            clientName: "dataverse-fsa-line-fetcher");

        services.AddSingleton<IFsaLineFetcher>(sp => sp.GetRequiredService<FsaLineFetcher>());

        AddDataversePolicies(
            services.AddHttpClient<Infrastructure.Clients.FsWorkOrderAttachmentHttpClient>((sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<FsOptions>>().Value;
                http.BaseAddress = new Uri(opt.DataverseApiBaseUrl!, UriKind.Absolute);
            }).AddHttpMessageHandler<DataverseAuthHandler>(),
            clientName: "dataverse-wo-attachments");

        services.AddSingleton<IFsWorkOrderAttachmentClient>(sp =>
            sp.GetRequiredService<Infrastructure.Clients.FsWorkOrderAttachmentHttpClient>());
    }

    private static void RegisterFscmPostingClients(this IServiceCollection services)
    {
        RegisterTypedFscmClient<PostingHttpClient, IPostingClient>(services, "fscm-posting-http");
        RegisterTypedFscmClient<FscmSingleWorkOrderHttpClient, ISingleWorkOrderPostingClient>(services, "fscm-single-workorder");
        RegisterTypedFscmClient<FscmWorkOrderStatusUpdateHttpClient, IWorkOrderStatusUpdateClient>(services, "fscm-workorder-status-update");
        RegisterTypedFscmClient<FscmProjectStatusHttpClient, IFscmProjectStatusClient>(services, "fscm-project-status");
        RegisterTypedFscmClient<FscmDocuAttachmentsHttpClient, IFscmDocuAttachmentsClient>(services, "fscm-docu-attachments");
        RegisterTypedFscmClient<FscmInvoiceAttributesHttpClient, IFscmInvoiceAttributesClient>(services, "fscm-invoice-attributes");

        services.AddSingleton<PayloadPostingDateAdjuster>();
        services.AddSingleton<DocumentAttachmentCopyRunner>();
        services.AddSingleton<IFscmPostRequestFactory, FscmPostRequestFactory>();
        services.AddSingleton<IWoPayloadNormalizer, WoPayloadNormalizer>();
        services.AddSingleton<IWoPayloadShapeGuard, WoPayloadShapeGuard>();
        services.AddSingleton<IWoJournalProjector, WoJournalProjector>();
        services.AddSingleton<IFscmPostingResponseParser, FscmPostingResponseParserAdapter>();
        services.AddSingleton<IPostErrorAggregator, PostErrorAggregator>();
        services.AddSingleton<IPostingWorkflowFactory, PostingWorkflowFactory>();

        services.AddSingleton<IFscmJournalFetchPolicy, ItemJournalFetchPolicy>();
        services.AddSingleton<IFscmJournalFetchPolicy, ExpenseJournalFetchPolicy>();
        services.AddSingleton<IFscmJournalFetchPolicy, HourJournalFetchPolicy>();
        services.AddSingleton<FscmJournalFetchPolicyResolver>();
        services.AddSingleton<IFscmJournalLineRowMapper, FscmJournalLineRowMapper>();
    }

    private static void RegisterFscmReferenceClients(this IServiceCollection services, IConfiguration cfg)
    {
        RegisterTypedFscmClient<FscmJournalFetchHttpClient, IFscmJournalFetchClient>(services, "fscm-journal-fetch");
        RegisterTypedFscmClient<FscmAccountingPeriodHttpClient, IFscmAccountingPeriodClient>(services, "fscm-accounting-period");
        RegisterTypedFscmClient<FscmLegalEntityIntegrationParametersHttpClient, IFscmLegalEntityIntegrationParametersClient>(services, "fscm-legal-entity-integration-params");

        AddFscmPolicies(
            services.AddHttpClient<FscmReleasedDistinctProductsHttpClient>((sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<FscmOptions>>().Value;
                http.BaseAddress = new Uri(opt.BaseUrl!, UriKind.Absolute);
            }).AddHttpMessageHandler<FscmAuthHandler>(),
            clientName: "fscm-released-distinct-products");

        services.AddMemoryCache();
        services.AddOptions<FscmReleasedDistinctProductsCacheOptions>()
            .Bind(cfg.GetSection(FscmReleasedDistinctProductsCacheOptions.SectionName))
            .Validate(o => o.Ttl > TimeSpan.Zero, "Fscm:ReleasedDistinctProductsCache:Ttl must be > 0")
            .Validate(o => o.NegativeTtl > TimeSpan.Zero, "Fscm:ReleasedDistinctProductsCache:NegativeTtl must be > 0")
            .Validate(o => o.MaxItemCountPerCall > 0, "Fscm:ReleasedDistinctProductsCache:MaxItemCountPerCall must be > 0")
            .ValidateOnStart();

        services.AddHttpClient<FscmReleasedDistinctProductsHttpClient>();
        services.AddSingleton<IFscmReleasedDistinctProductsClient>(sp =>
            new CachedFscmReleasedDistinctProductsClient(
                sp.GetRequiredService<FscmReleasedDistinctProductsHttpClient>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<IOptions<FscmReleasedDistinctProductsCacheOptions>>(),
                sp.GetRequiredService<ILogger<CachedFscmReleasedDistinctProductsClient>>()));

        RegisterTypedFscmClient<FscmCustomValidationClient, IFscmCustomValidationClient>(services, "fscm-custom-validation");

        AddFscmPolicies(
            services.AddHttpClient<FscmWoPayloadValidationClient>((sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<FscmOptions>>().Value;
                http.BaseAddress = new Uri(opt.BaseUrl!, UriKind.Absolute);
            }).AddHttpMessageHandler<FscmAuthHandler>(),
            clientName: "fscm-wo-payload-validation");
        services.AddSingleton<IFscmWoPayloadValidationClient>(sp => sp.GetRequiredService<FscmWoPayloadValidationClient>());

        RegisterTypedFscmClient<FscmSubProjectHttpClient, IFscmSubProjectClient>(services, "fscm-subproject");
        RegisterTypedFscmClient<FscmBaselineFetcher, IFscmBaselineFetcher>(services, "fscm-baseline-fetcher");
        RegisterTypedFscmClient<FscmGlobalAttributeMappingHttpClient, IFscmGlobalAttributeMappingClient>(services, "fscm-global-attribute-mapping");

        services.AddSingleton<IJournalDescriptionBuilder, JournalDescriptionBuilder>();
    }

    private static void RegisterTypedFscmClient<TImplementation, TContract>(IServiceCollection services, string clientName)
        where TContract : class
        where TImplementation : class, TContract
    {
        AddFscmPolicies(
            services.AddHttpClient<TImplementation>((sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<FscmOptions>>().Value;
                http.BaseAddress = new Uri(opt.BaseUrl!, UriKind.Absolute);
            }).AddHttpMessageHandler<FscmAuthHandler>(),
            clientName);

        services.AddSingleton<TContract>(sp => sp.GetRequiredService<TImplementation>());
    }

    private static IHttpClientBuilder AddDataversePolicies(IHttpClientBuilder builder, string clientName)
    {
        return builder
            .ConfigureHttpClient((sp, client) =>
            {
                var opt = sp.GetRequiredService<IOptions<HttpPolicyOptions>>().Value.Dataverse;
                client.Timeout = TimeSpan.FromSeconds(opt.TimeoutSeconds);
            })
            .AddPolicyHandler((sp, req) =>
            {
                if (req?.Method == HttpMethod.Post)
                    return Policy.NoOpAsync<HttpResponseMessage>();

                var policies = sp.GetRequiredService<ResilienceHttpPolicies>();
                var opt = sp.GetRequiredService<IOptions<HttpPolicyOptions>>().Value.Dataverse;
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger($"DataversePolicy:{clientName}");
                return policies.CreateRetryPolicy(opt, logger, "Dataverse");
            });
    }

    private static IHttpClientBuilder AddFscmPolicies(IHttpClientBuilder builder, string clientName)
    {
        return builder
            .ConfigureHttpClient((sp, client) =>
            {
                var opt = sp.GetRequiredService<IOptions<HttpPolicyOptions>>().Value.Fscm;
                client.Timeout = TimeSpan.FromSeconds(opt.TimeoutSeconds);
            })
            .AddPolicyHandler((sp, req) =>
            {
                if (req?.Method == HttpMethod.Post)
                    return Policy.NoOpAsync<HttpResponseMessage>();

                var policies = sp.GetRequiredService<ResilienceHttpPolicies>();
                var opt = sp.GetRequiredService<IOptions<HttpPolicyOptions>>().Value.Fscm;
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger($"FscmPolicy:{clientName}");
                return policies.CreateRetryPolicy(opt, logger, "FSCM");
            });
    }
}
