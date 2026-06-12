using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.AI;
using ServiceHub.Infrastructure.BackgroundServices;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Persistence.InMemory;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Infrastructure.ServiceBus;

namespace ServiceHub.Infrastructure;

/// <summary>
/// Extension methods for registering infrastructure services with dependency injection.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds all infrastructure services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration (optional, will resolve from DI if not provided).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration? configuration = null)
    {
        // Service Bus
        services.AddServiceBus();

        // Persistence
        services.AddPersistence();

        // DLQ Intelligence database
        services.AddDlqDatabase(configuration);

        // Security (needs configuration for encryption key)
        services.AddSecurity(configuration);

        // AI
        services.AddAI();

        // Webhooks
        services.AddWebhooks(configuration);

        // Background Services — DlqMonitorWorker is also registered here for
        // direct AddInfrastructure callers that do not call AddBackgroundWorkers separately.
        // NOTE: do NOT add DlqMonitorWorker here; AddBackgroundWorkers registers it.
        // Historically this line caused a duplicate-worker bug.

        return services;
    }

    /// <summary>
    /// Adds Service Bus infrastructure services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddServiceBus(this IServiceCollection services)
    {
        services.TryAddSingleton<IServiceBusClientCache, ServiceBusClientCache>();
        services.TryAddScoped<IServiceBusClientFactory, ServiceBusClientFactory>();
        services.TryAddScoped<IMessageSender, MessageSender>();
        services.TryAddScoped<IMessageReceiver, MessageReceiver>();

        // Health check
        services.AddHealthChecks()
            .AddCheck<ServiceBusHealthCheck>("servicebus", tags: ["ready", "servicebus"]);

        return services;
    }

    /// <summary>
    /// Adds persistence infrastructure services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        // In-memory repository for MVP
        services.TryAddSingleton<INamespaceRepository, InMemoryNamespaceRepository>();

        return services;
    }

    /// <summary>
    /// Adds security infrastructure services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration (optional).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSecurity(this IServiceCollection services, IConfiguration? configuration = null)
    {
        // ConnectionStringProtector now requires IConfiguration
        services.TryAddSingleton<IConnectionStringProtector, ConnectionStringProtector>();

        return services;
    }

    /// <summary>
    /// Adds AI infrastructure services.
    /// <para>
    /// <see cref="IAIServiceClient"/> — singleton anomaly-detection client used by AnomaliesController
    /// for real-time message analysis. <br/>
    /// <see cref="IForensicEngine"/> — scoped three-tier forensic classifier registered in
    /// <see cref="AddDlqDatabase"/> because it operates on per-request <c>DlqMessage</c> entities.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAI(this IServiceCollection services)
    {
        services.TryAddSingleton<IAIServiceClient, AIServiceClient>();

        return services;
    }

    /// <summary>
    /// Adds background services for message polling and anomaly detection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBackgroundWorkers(this IServiceCollection services)
    {
        services.AddHostedService<MessagePollingWorker>();
        services.AddHostedService<AnomalyDetectionWorker>();
        services.AddHostedService<DlqMonitorWorker>();

        return services;
    }

    /// <summary>
    /// Adds the DLQ Intelligence SQLite database and related services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration (optional).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDlqDatabase(this IServiceCollection services, IConfiguration? configuration = null)
    {
        var dataDir = configuration?["DlqDatabase:DataDirectory"]
            ?? Path.Combine(AppContext.BaseDirectory, "data");

        Directory.CreateDirectory(dataDir);

        var dbPath = Path.Combine(dataDir, "servicehub-dlq.db");
        var connectionString = $"Data Source={dbPath}";

        services.AddDbContext<DlqDbContext>((serviceProvider, options) =>
        {
            options.UseSqlite(connectionString);

            // EnableDetailedErrors surfaces EF Core internals (SQL, schema) in error messages.
            // Only enable in Development to prevent information leakage in production.
            var env = serviceProvider.GetService<IHostEnvironment>();
            if (env?.IsDevelopment() == true)
            {
                options.EnableDetailedErrors();
            }
        });

        // Register DLQ services
        services.TryAddScoped<IDlqMonitorService, DlqMonitorService>();
        services.TryAddScoped<IDlqHistoryService, DlqHistoryService>();
        services.TryAddScoped<IRuleEngine, RuleEngine>();
        services.TryAddScoped<IAutoReplayExecutor, AutoReplayExecutor>();
        services.TryAddScoped<IForensicEngine, ForensicEngine>();

        return services;
    }

    /// <summary>
    /// Adds webhook notification services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration (optional).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddWebhooks(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.Configure<WebhookOptions>(opts =>
            configuration?.GetSection(WebhookOptions.SectionName).Bind(opts));

        services.AddHttpClient<IWebhookNotifier, WebhookNotifier>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
