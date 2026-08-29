using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.AI;
using ServiceHub.Infrastructure.Backup;
using ServiceHub.Infrastructure.BackgroundServices;
using ServiceHub.Infrastructure.BulkOperations;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Persistence.InMemory;
using ServiceHub.Infrastructure.RecoveryLedger;
using ServiceHub.Infrastructure.Routing;
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
        services.AddAI(configuration);

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

        // Also expose ICloudProviderRouter to the same scoped instance, so the Api layer
        // (controllers) can depend on the Core interface instead of the concrete
        // Infrastructure type — mirrors the IPlatformEventBus/InProcessPlatformEventBus
        // pattern below. Infrastructure-internal consumers keep resolving the concrete type.
        services.TryAddScoped<Core.Interfaces.ICloudProviderRouter>(
            sp => sp.GetRequiredService<ServiceHub.Infrastructure.Routing.CloudProviderRouter>());

        // Health check
        services.AddHealthChecks()
            .AddCheck<ServiceBusHealthCheck>("servicebus", tags: ["ready", "servicebus"]);

        return services;
    }

    /// <summary>
    /// Registers the Azure Service Bus <see cref="ICloudMessagingProvider"/> so the
    /// <c>CloudProviderRouter</c> can dispatch operations for Azure namespaces.
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
    /// <param name="configuration">The configuration (optional).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAI(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.Configure<Core.Models.AIServiceOptions>(opts =>
            configuration?.GetSection(Core.Models.AIServiceOptions.SectionName).Bind(opts));

        services.AddHttpClient(AI.AIServiceClient.HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<Core.Models.AIServiceOptions>>().Value;
            if (Uri.TryCreate(options.ServiceUrl, UriKind.Absolute, out var baseUri))
            {
                client.BaseAddress = baseUri;
            }

            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        services.TryAddSingleton<IAIServiceClient, AIServiceClient>();

        // Feature extraction and fingerprinting services (strategy-independent layer)
        services.TryAddScoped<IFailureFeatureExtractor, AI.FailureFeatureExtractor>();
        services.TryAddScoped<IFailureFingerprintBuilder, AI.FailureFingerprintBuilder>();

        // Signature recognition service (business-level layer)
        services.TryAddScoped<IFailureSignatureRecognitionService, AI.FailureSignatureRecognitionService>();

        return services;
    }

    /// <summary>
    /// Adds background services for anomaly detection, DLQ monitoring, bulk operations,
    /// signature replay, and audit-log retention.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBackgroundWorkers(this IServiceCollection services)
    {
        services.AddHostedService<AnomalyDetectionWorker>();
        services.AddHostedService<DriftDetectionWorker>();
        services.AddHostedService<CorrelationDetectionWorker>();
        services.AddHostedService<NarrationWorker>();
        services.AddHostedService<DlqMonitorWorker>();
        services.AddHostedService<BulkOperationWorker>();
        services.AddHostedService<SignatureReplayWorker>();
        services.AddHostedService<AuditRetentionWorker>();
        services.AddHostedService<RecoveryVerificationWorker>();
        services.AddHostedService<RecoveryAgeingWorker>();
        services.AddHostedService<AutonomyEvaluationWorker>();
        services.AddHostedService<BackupWorker>();

        // Self-observability of the autonomy machinery itself (roadmap §6, cross-cutting
        // foundation item 4) — registered alongside the workers above as a matched unit, so an
        // environment that skips AddBackgroundWorkers() (no workers running) also skips the
        // health check that watches them, rather than reporting every worker perpetually
        // "never reported".
        services.TryAddSingleton<IWorkerHeartbeatStore, InMemoryWorkerHeartbeatStore>();
        services.TryAddSingleton(serviceProvider =>
        {
            var resolvedConfiguration = serviceProvider.GetRequiredService<IConfiguration>();
            return new WorkerHeartbeatHealthCheckOptions
            {
                StalenessMultiplier = resolvedConfiguration.GetValue(
                    "WorkerHeartbeat:StalenessMultiplier",
                    WorkerHeartbeatHealthCheckOptions.Default.StalenessMultiplier),
            };
        });
        services.AddHealthChecks()
            .AddCheck<WorkerHeartbeatHealthCheck>("worker-heartbeat", tags: ["workers"]);

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

            // WAL journaling + busy_timeout (roadmap F1) — eight independent background
            // workers write to this single file with no such configuration otherwise.
            var busyTimeoutMilliseconds = resolvedConfiguration.GetValue(
                "DlqDatabase:BusyTimeoutMilliseconds",
                SqlitePragmaConnectionInterceptor.DefaultBusyTimeoutMilliseconds);
            options.AddInterceptors(new SqlitePragmaConnectionInterceptor(busyTimeoutMilliseconds));

            // EnableDetailedErrors surfaces EF Core internals (SQL, schema) in error messages.
            // Only enable in Development to prevent information leakage in production.
            var env = serviceProvider.GetService<IHostEnvironment>();
            if (env?.IsDevelopment() == true)
            {
                options.EnableDetailedErrors();
            }
        });

        // Single-instance invariant (roadmap W1.4) — Singleton so the OS-level file lock it
        // acquires in its constructor is held for the process lifetime and released by the
        // container on shutdown. Program.cs resolves this eagerly at startup so a second
        // instance against the same data directory fails fast, before any database access.
        services.TryAddSingleton(serviceProvider =>
            new SqliteInstanceLock(serviceProvider.GetRequiredService<IConfiguration>()));

        // SaveChanges retry tunables (roadmap F1) — Singleton is fine: Scoped DlqDbContext
        // instances may depend on a Singleton, and the retry attempt count has no per-request
        // state of its own.
        services.TryAddSingleton(serviceProvider =>
        {
            var resolvedConfiguration = serviceProvider.GetRequiredService<IConfiguration>();
            var maxRetryAttempts = resolvedConfiguration.GetValue(
                "DlqDatabase:MaxBusyRetryAttempts",
                SqliteBusyRetryOptions.Default.MaxRetryAttempts);
            return new SqliteBusyRetryOptions { MaxRetryAttempts = maxRetryAttempts };
        });

        // Basic DB observability (roadmap §8 F-track item 4) — file size, WAL-checkpoint
        // status, slow-query-equivalent logging, surfaced through the health check
        // infrastructure that already exists. Singleton for the same reason as
        // SqliteBusyRetryOptions above.
        services.TryAddSingleton(serviceProvider =>
        {
            var resolvedConfiguration = serviceProvider.GetRequiredService<IConfiguration>();
            return new SqliteDatabaseHealthCheckOptions
            {
                WalSizeWarningThresholdBytes = resolvedConfiguration.GetValue(
                    "DlqDatabase:HealthCheck:WalSizeWarningThresholdBytes",
                    SqliteDatabaseHealthCheckOptions.Default.WalSizeWarningThresholdBytes),
                SlowCheckThreshold = TimeSpan.FromMilliseconds(resolvedConfiguration.GetValue(
                    "DlqDatabase:HealthCheck:SlowCheckThresholdMilliseconds",
                    SqliteDatabaseHealthCheckOptions.Default.SlowCheckThreshold.TotalMilliseconds))
            };
        });

        services.AddHealthChecks()
            .AddCheck<SqliteDatabaseHealthCheck>("sqlite", tags: ["ready", "database"]);

        // Register DLQ services
        services.TryAddSingleton<DlqNotMonitoredLogGuard>();
        services.TryAddScoped<IDlqMonitorService, DlqMonitorService>();
        services.TryAddScoped<IDlqHistoryService, DlqHistoryService>();
        services.TryAddScoped<INamespaceSignatureLookupService, NamespaceSignatureLookupService>();
        services.TryAddScoped<IAnomalyDetectionService, Analytics.DeterministicAnomalyDetectionService>();
        services.TryAddSingleton<IAnomalyResultCache, Analytics.InMemoryAnomalyResultCache>();
        services.TryAddScoped<IDriftDetectionService, Analytics.DeterministicDriftDetectionService>();
        services.TryAddSingleton<IDriftResultCache, Analytics.InMemoryDriftResultCache>();
        services.TryAddScoped<IContractViolationExportService, Analytics.DeterministicContractViolationExportService>();
        services.TryAddScoped<ICorrelationDetectionService, Analytics.DeterministicCorrelationDetectionService>();
        services.TryAddSingleton<ICorrelationResultCache, Analytics.InMemoryCorrelationResultCache>();
        services.TryAddScoped<INarrationService, Analytics.DeterministicNarrationService>();
        services.TryAddSingleton<INarrationResultCache, Analytics.InMemoryNarrationResultCache>();

        // Register signature analysis strategies.
        // AIClusteringStrategy wraps the AI service client and provides rich clustering.
        // DeterministicClusteringStrategy provides reliable fallback clustering without
        // external dependencies. Both implement ISignatureAnalysisStrategy.
        services.TryAddScoped<AI.AIClusteringStrategy>();
        services.TryAddScoped<AI.DeterministicClusteringStrategy>();

        // DlqSignatureAnalysisService orchestrates both strategies, trying AI first
        // and falling back to deterministic if AI is unavailable.
        services.TryAddScoped<IDlqSignatureAnalysisService, AI.DlqSignatureAnalysisService>();

        // Failure Knowledge service for operational memory
        services.TryAddScoped<IFailureKnowledgeService, FailureKnowledgeService>();

        // Failure Signature lifecycle (Active/Resolved/Reopened/Suppressed/Archived) — EF Core
        // (DlqDbContext)-backed, so Scoped like every other DlqDbContext-backed service.
        services.TryAddScoped<ISignatureLifecycleService, SignatureLifecycleService>();

        // Recovery Evidence Ledger — EF Core (DlqDbContext)-backed, so Scoped like every other
        // DlqDbContext-backed service. No callers yet; wired to the recovery paths in a later phase.
        services.TryAddScoped<IRecoveryLedger, RecoveryLedgerService>();
        services.TryAddScoped<IRecoveryEvidenceExporter, RecoveryEvidenceExporter>();

        // Deterministic Eligibility Gate (roadmap §9/Phase B) — the single safety-decision point
        // every recovery attempt passes through before a provider call.
        services.TryAddScoped<IRecoveryEligibilityGate, RecoveryEligibilityGate>();

        // Evidence-Derived Trust Scoring (roadmap §8.10/Phase C) — read-only aggregation over
        // the ledger; never writes, never grants autonomy.
        services.TryAddScoped<IRecoveryTrustScoringService, RecoveryTrustScoringService>();

        // Approval Queue (roadmap §11 item 1) — read-only view over rule-triggered Escalate
        // declines; approval itself reuses the existing single-message replay endpoint, so this
        // service never writes.
        services.TryAddScoped<IApprovalQueueService, ApprovalQueueService>();

        services.TryAddScoped<IFleetOverviewService, FleetOverviewService>();

        // Fleet-wide autonomy dashboard (roadmap §11 item 5, §15 item 9) — read-only aggregation
        // over AutonomyGrants/AutoReplayRules/RecoveryEvents; never writes, never grants autonomy.
        services.TryAddScoped<IAutonomyDashboardService, AutonomyDashboardService>();

        services.TryAddScoped<IRuleEngine, RuleEngine>();
        services.TryAddScoped<IAutoReplayExecutor, AutoReplayExecutor>();

        // Forensic engine — base engine registered both unkeyed (so provider-specific decorators
        // like AwsForensicEngine/GcpForensicEngine can resolve it as their base-engine dependency)
        // and keyed under CloudProviderType.Azure (so ForensicEngineRouter can resolve it
        // uniformly alongside any provider-specific engines registered elsewhere). Callers that
        // need provider-aware dispatch should depend on IForensicEngineRouter, not IForensicEngine
        // directly — see DlqMonitorService.
        services.TryAddScoped<IForensicEngine, ForensicEngine>();
        services.TryAddKeyedScoped<IForensicEngine, ForensicEngine>(CloudProviderType.Azure);
        services.TryAddScoped<IForensicEngineRouter, ForensicEngineRouter>();

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

        // Signature Replay — same queue/worker split as Bulk Operations: the queue is a
        // singleton (process-wide hand-off + cancellation registry), the service and executor
        // are scoped like every other DlqDbContext-backed service. The worker itself is
        // registered by AddBackgroundWorkers(), not here.
        services.TryAddSingleton<ISignatureReplayQueue, SignatureReplay.SignatureReplayQueue>();
        services.TryAddScoped<ISignatureReplayService, SignatureReplay.SignatureReplayService>();
        services.TryAddScoped<ISignatureReplayExecutor, SignatureReplay.SignatureReplayExecutor>();

        // Failure Intelligence Center — aggregation service for incident command center
        services.TryAddScoped<IFailureIntelligenceCenterService, FailureIntelligenceCenterService>();

        // Backup & Restore (roadmap F2) — Scoped like every other DlqDbContext-backed service;
        // BackupWorker creates its own scope per scheduled run (same pattern as DlqMonitorWorker).
        // BackupOptions itself is bound + validated by
        // ConfigurationValidationExtensions.AddServiceHubConfigurationValidation (mirrors
        // AuditRetentionOptions), not here.
        services.TryAddScoped<IBackupService, BackupService>();

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

        // WebhookAutonomyTransitionHandler bridges AutonomyGrantTransitioned events to
        // IWebhookNotifier — same pattern as WebhookDlqSpikeHandler above.
        services.AddSingleton<WebhookAutonomyTransitionHandler>();

        // WebhookCircuitBreakerTrippedHandler bridges AutoReplayRuleCircuitBreakerTripped
        // events to IWebhookNotifier — same pattern as WebhookDlqSpikeHandler above.
        services.AddSingleton<WebhookCircuitBreakerTrippedHandler>();

        // WebhookInsightDetectedHandler bridges InsightDetected events (roadmap §5, I5 — "Push")
        // to IWebhookNotifier — same pattern as WebhookDlqSpikeHandler above.
        services.AddSingleton<WebhookInsightDetectedHandler>();

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

        var autonomyTransitionWebhookHandler = serviceProvider.GetRequiredService<WebhookAutonomyTransitionHandler>();
        bus.Subscribe(autonomyTransitionWebhookHandler.HandleAsync);

        var circuitBreakerWebhookHandler = serviceProvider.GetRequiredService<WebhookCircuitBreakerTrippedHandler>();
        bus.Subscribe(circuitBreakerWebhookHandler.HandleAsync);

        var insightDetectedWebhookHandler = serviceProvider.GetRequiredService<WebhookInsightDetectedHandler>();
        bus.Subscribe(insightDetectedWebhookHandler.HandleAsync);
    }
}
