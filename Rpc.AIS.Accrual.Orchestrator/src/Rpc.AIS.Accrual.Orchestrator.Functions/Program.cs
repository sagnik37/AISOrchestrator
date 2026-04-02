// File: src/Rpc.AIS.Accrual.Orchestrator.Functions/Program.cs
// .NET 8 isolated worker host bootstrap + DI registrations.

using System;
using System.Configuration;
using System.Linq;
using System.Net.Http;

using Azure.Communication.Email;
using Azure.Identity;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.ApplicationInsights;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Polly;

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
using Rpc.AIS.Accrual.Orchestrator.Functions.DependencyInjection;
//ing Rpc.AIS.Accrual.Orchestrator.Functions.Extensions;
using Rpc.AIS.Accrual.Orchestrator.Functions.Services;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.FscmJournalPolicies;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients.Posting;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Http;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Logging;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Notifications;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Resilience;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Utilities;

using CoreNotificationOptions = Rpc.AIS.Accrual.Orchestrator.Core.Options.NotificationOptions;
using InfraNotificationOptions = Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options.NotificationOptions;

namespace Rpc.AIS.Accrual.Orchestrator.Functions;

internal static class Program
{
    public static void Main(string[] args)
    {
        var host = new HostBuilder()
            .ConfigureAppConfiguration((ctx, config) =>
            {
                // local.settings.json is loaded by the Functions host. appsettings.json is optional for local/dev convenience.
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                      .AddEnvironmentVariables();

                // Add Key Vault on top (Managed Identity / DefaultAzureCredential).
                var built = config.Build();
                var vaultUri = built["KeyVault:VaultUri"] ?? built["KeyVault:Uri"];

                if (!string.IsNullOrWhiteSpace(vaultUri))
                {
                    config.AddAzureKeyVault(new Uri(vaultUri), new DefaultAzureCredential());
                }
                else if (!string.Equals(ctx.HostingEnvironment.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("KeyVault:VaultUri is required in non-Development environments.");
                }
            })
            .ConfigureFunctionsWorkerDefaults()
            .ConfigureServices((ctx, services) =>
            {
                var cfg = ctx.Configuration;

                services.AddObservability(cfg);
                services.AddCoreServices(cfg);
                services.AddEnrichmentPipeline();
                services.AddInfrastructureClients(cfg);
            })
            .Build();

        host.Run();
    }
}
