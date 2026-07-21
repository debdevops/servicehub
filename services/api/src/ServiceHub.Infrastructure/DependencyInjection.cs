using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.AI;
using ServiceHub.Infrastructure.BackgroundServices;
using ServiceHub.Infrastructure.BulkOperations;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Persistence.InMemory;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Infrastructure.Events;
using ServiceHub.Infrastructure.Events.Handlers;
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

        // Platform Events
        services.AddPlatformEvents();

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
        services.AddScoped<IMessageOperationsService, MessageOperationsService>();
        services.AddScoped<Core.Interfaces.ILiveTailSessionFactory, LiveTail.LiveTailSessionFactory>();
        services.AddSingleton<Core.Interfaces.ILiveTailConnectionLimiter, LiveTail.LiveTailConnectionLimiter>();

        // Router depends on all registered ICloudMessagingProvider implementations.
        // Scoped (not singleton) because live providers such as AzureMessagingProvider are
        // scoped — a root-built singleton cannot resolve them under scope validation.
        services.TryAddScoped(sp => new ServiceHub.Infrastructure.Routing.CloudProviderRouter(sp.GetServices<ICloudMessagingProvider>()));

        // Health check
        services.AddHealthChecks()
            .AddCheck<ServiceBusHealthCheck>("servicebus", tags: ["ready", "servicebus"]);

        return services;
    }

    /// <summary>
    /// Registers the Azure Service Bus <see cref="ICloudMessagingProvider"/> so the
    /// <c>CloudProviderRouter</c> can dispatch operations for Azure namespaces.
    /// Do not call this in Simulator mode — <c>AddSimulatorProviders()</c> registers a
    /// simulated Azure provider and the router rejects duplicate provider types.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAzureProvider(this IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<ICloudMessagingProvider, Azure.AzureMessagingProvider>());

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
    /// Adds background services for anomaly detection, DLQ monitoring, and bulk operations.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBackgroundWorkers(this IServiceCollection services)
    {
        services.AddHostedService<AnomalyDetectionWorker>();
        services.AddHostedService<DlqMonitorWorker>();
        services.AddHostedService<BulkOperationWorker>();

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
        services.AddDbContext<DlqDbContext>((serviceProvider, options) =>
        {
            // Resolved lazily from the fully-built DI container instead of the
            // eagerly-captured `configuration` parameter: at AddDlqDatabase's registration
            // time (during Program.cs's synchronous top-level startup), a test host's
            // ConfigureAppConfiguration overrides (e.g. DlqDatabase:DataDirectory) have not
            // necessarily been layered in yet, so reading them here would silently fall back
            // to the shared default path — causing concurrent test hosts to race on the same
            // SQLite file. Resolving IConfiguration from the built container guarantees the
            // final, fully-merged configuration is used.
            var resolvedConfiguration = serviceProvider.GetRequiredService<IConfiguration>();
            var dataDir = resolvedConfiguration["DlqDatabase:DataDirectory"]
                ?? Path.Combine(AppContext.BaseDirectory, "data");
            Directory.CreateDirectory(dataDir);

            var dbPath = Path.Combine(dataDir, "servicehub-dlq.db");
            options.UseSqlite($"Data Source={dbPath}");

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
        services.TryAddScoped<IFleetOverviewService, FleetOverviewService>();
        services.TryAddScoped<IRuleEngine, RuleEngine>();
        services.TryAddScoped<IAutoReplayExecutor, AutoReplayExecutor>();
        services.TryAddScoped<IForensicEngine, ForensicEngine>();

        // Audit Trail — registered as singleton so the channel is shared across all
        // request scopes. The BackgroundService lifetime matches the application lifetime.
        services.AddSingleton<AuditService>();
        services.AddSingleton<IAuditService>(sp => sp.GetRequiredService<AuditService>());
        services.AddHostedService(sp => sp.GetRequiredService<AuditService>());

        // Bulk Operations — the queue is a singleton (process-wide hand-off + cancellation
        // registry, mirrors AuditService/InProcessPlatformEventBus); the service and executor
        // are scoped like every other DlqDbContext-backed service. The worker itself is
        // registered by AddBackgroundWorkers(), not here — same split DlqMonitorWorker uses.
        services.TryAddSingleton<IBulkOperationQueue, BulkOperationQueue>();
        services.TryAddScoped<IBulkOperationService, BulkOperationService>();
        services.TryAddScoped<IBulkOperationExecutor, BulkOperationExecutor>();

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

        // One IWebhookMessageFormatter per WebhookFormat — WebhookNotifier selects among them
        // at send time based on WebhookOptions.Format. A future destination format is a new
        // registration here, not a change to WebhookNotifier or any existing formatter.
        services.AddSingleton<Core.Interfaces.IWebhookMessageFormatter, Webhooks.GenericWebhookFormatter>();
        services.AddSingleton<Core.Interfaces.IWebhookMessageFormatter, Webhooks.SlackWebhookFormatter>();
        services.AddSingleton<Core.Interfaces.IWebhookMessageFormatter, Webhooks.TeamsWebhookFormatter>();

        services.AddHttpClient<IWebhookNotifier, WebhookNotifier>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }

    /// <summary>
    /// Adds the internal in-process Platform Event bus.
    /// <para>
    /// The <see cref="InProcessPlatformEventBus"/> is registered as a singleton so that
    /// the underlying <see cref="System.Threading.Channels.Channel{T}"/> is shared across
    /// all request scopes. It is also registered as an
    /// <see cref="Microsoft.Extensions.Hosting.IHostedService"/> so that its drain loop
    /// starts with the application — identical to the AuditService registration pattern.
    /// </para>
    /// <para>
    /// Subscriber handlers are registered as singletons and wired to the bus here
    /// via <see cref="Core.Interfaces.IPlatformEventBus.Subscribe"/>.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPlatformEvents(this IServiceCollection services)
    {
        // Register the concrete bus as a singleton.
        services.AddSingleton<InProcessPlatformEventBus>();

        // Expose IPlatformEventBus to the same singleton instance.
        services.AddSingleton<Core.Interfaces.IPlatformEventBus>(
            sp => sp.GetRequiredService<InProcessPlatformEventBus>());

        // Register the drain-loop BackgroundService against the same singleton instance.
        services.AddHostedService(
            sp => sp.GetRequiredService<InProcessPlatformEventBus>());

        // ── Subscribers ───────────────────────────────────────────────────────

        // WebhookDlqSpikeHandler bridges DlqSpikeDetected events to IWebhookNotifier.
        // Registered as a singleton; creates its own DI scope per invocation to
        // safely resolve the scoped IWebhookNotifier dependency.
        services.AddSingleton<WebhookDlqSpikeHandler>();

        // WebhookBulkOperationCompletedHandler bridges BulkOperationCompleted events to
        // IWebhookNotifier — same pattern as WebhookDlqSpikeHandler above.
        services.AddSingleton<WebhookBulkOperationCompletedHandler>();

        return services;
    }

    /// <summary>
    /// Wires all registered Platform Event subscribers to the bus.
    /// Must be called once after the <see cref="IServiceProvider"/> is built,
    /// typically from the application startup (e.g. <c>Program.cs</c> or
    /// a hosted service startup hook).
    /// </summary>
    /// <param name="serviceProvider">The built service provider.</param>
    public static void SubscribePlatformEventHandlers(this IServiceProvider serviceProvider)
    {
        var bus = serviceProvider.GetRequiredService<Core.Interfaces.IPlatformEventBus>();
        var webhookHandler = serviceProvider.GetRequiredService<WebhookDlqSpikeHandler>();
        bus.Subscribe(webhookHandler.HandleAsync);

        var bulkOperationWebhookHandler = serviceProvider.GetRequiredService<WebhookBulkOperationCompletedHandler>();
        bus.Subscribe(bulkOperationWebhookHandler.HandleAsync);
    }
}
