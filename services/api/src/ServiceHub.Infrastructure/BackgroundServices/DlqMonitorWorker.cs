using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Events;
using ServiceHub.Core.Events.Payloads;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Background worker that periodically scans all registered namespace DLQs.
/// Polls active namespaces on a fixed interval.
/// </summary>
public sealed class DlqMonitorWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IPlatformEventBus _eventBus;
    private readonly ILogger<DlqMonitorWorker> _logger;

    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(5);  // Fast startup
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);  // Aggressive polling for near-realtime DLQ detection
    private static readonly int MaxParallelScans = 10;

    /// <summary>
    /// Initializes a new instance of the <see cref="DlqMonitorWorker"/> class.
    /// </summary>
    /// <param name="serviceProvider">Root service provider for per-scan-cycle scope creation.</param>
    /// <param name="logger">Logger instance.</param>
    public DlqMonitorWorker(
        IServiceProvider serviceProvider,
        ILogger<DlqMonitorWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // IPlatformEventBus is a singleton — resolve once from the root provider.
        // This avoids resolving it from a scoped context on every poll cycle.
        _eventBus = serviceProvider.GetRequiredService<IPlatformEventBus>();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DLQ Monitor Worker starting. Initial delay: {Delay}s", InitialDelay.TotalSeconds);

        // Ensure the database is created
        try
        {
            using var initScope = _serviceProvider.CreateScope();
            var dbContext = initScope.ServiceProvider.GetRequiredService<DlqDbContext>();
            await dbContext.Database.EnsureCreatedAsync(stoppingToken);
            _logger.LogInformation("DLQ Intelligence database initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize DLQ Intelligence database");
            return;
        }

        await Task.Delay(InitialDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var namespaceRepo = scope.ServiceProvider.GetRequiredService<INamespaceRepository>();

                var namespacesResult = await namespaceRepo.GetActiveAsync(stoppingToken);
                if (namespacesResult.IsFailure)
                {
                    _logger.LogWarning("Failed to get active namespaces: {Error}", namespacesResult.Error.Message);
                    await Task.Delay(PollInterval, stoppingToken);
                    continue;
                }

                var allActive = namespacesResult.Value;

                // DLQ monitoring is currently implemented only for Azure Service Bus.
                // Skip non-Azure namespaces here so we don't attempt (and fail) an Azure
                // client build against them every poll cycle. ScanNamespaceAsync also guards
                // this, but filtering up front avoids per-cycle failure noise.
                var namespaces = allActive
                    .Where(n => n.Provider == ServiceHub.Core.Enums.CloudProviderType.Azure)
                    .ToList();

                var skipped = allActive.Count - namespaces.Count;
                if (skipped > 0)
                {
                    _logger.LogDebug(
                        "Skipping {Skipped} non-Azure namespace(s) — DLQ monitoring currently supports Azure Service Bus only",
                        skipped);
                }

                if (namespaces.Count == 0)
                {
                    _logger.LogInformation("No active Azure namespaces found, sleeping for {Interval}s", PollInterval.TotalSeconds);
                    await Task.Delay(PollInterval, stoppingToken);
                    continue;
                }

                _logger.LogInformation("Scanning DLQs for {Count} namespace(s): {Namespaces}", 
                    namespaces.Count, 
                    string.Join(", ", namespaces.Select(n => $"{LogRedactor.SanitiseForLog(n.Name)} (ID: {n.Id})")));

                    using var semaphore = new SemaphoreSlim(MaxParallelScans);
                var tasks = namespaces.Select(async ns =>
                {
                    await semaphore.WaitAsync(stoppingToken);
                    try
                    {
                        using var innerScope = _serviceProvider.CreateScope();
                        var monitor = innerScope.ServiceProvider.GetRequiredService<IDlqMonitorService>();
                        var scanResult = await monitor.ScanNamespaceAsync(ns.Id, stoppingToken);

                        // Publish a Platform Event when a DLQ spike is detected.
                        // The WebhookDlqSpikeHandler subscriber delivers the webhook notification.
                        // Publish-after-confirm: only fires when scanResult.Value > 0.
                        if (scanResult.IsSuccess && scanResult.Value > 0)
                        {
                            var payload = new DlqSpikeDetectedPayload
                            {
                                NamespaceId = ns.Id,
                                NamespaceName = ns.Name,
                                NewMessageCount = scanResult.Value,
                                DetectedAtUtc = DateTimeOffset.UtcNow,
                            };

                            var evt = new PlatformEvent
                            {
                                Source = "ServiceHub.Infrastructure.BackgroundServices.DlqMonitorWorker",
                                Category = EventCategories.Dlq,
                                EventType = EventTypes.DlqSpikeDetected,
                                Severity = EventSeverity.Warning,
                                CloudProvider = ns.Provider.ToString().ToLowerInvariant(),
                                NamespaceId = ns.Id,
                                NamespaceName = ns.Name,
                                Payload = payload,
                            };

                            await _eventBus.PublishAsync(evt, stoppingToken);

                            _logger.LogDebug(
                                "Published Platform Event {EventType} for NamespaceId {NamespaceId}",
                                evt.EventType, ns.Id);
                        }

                        // Evaluate auto-replay rules against active DLQ messages
                        try
                        {
                            var ruleEngine = innerScope.ServiceProvider.GetRequiredService<IRuleEngine>();
                            var replayExecutor = innerScope.ServiceProvider.GetRequiredService<IAutoReplayExecutor>();
                            var dbContext = innerScope.ServiceProvider.GetRequiredService<Persistence.DlqDbContext>();

                            var enabledRules = await dbContext.AutoReplayRules
                                .Where(r => r.Enabled)
                                .ToListAsync(stoppingToken);

                            if (enabledRules.Count > 0)
                            {
                                var activeMessages = await dbContext.DlqMessages
                                    .Where(m => m.NamespaceId == ns.Id
                                                && m.Status == Core.Enums.DlqMessageStatus.Active)
                                    .ToListAsync(stoppingToken);

                                foreach (var message in activeMessages)
                                {
                                    var matchingRules = ruleEngine.FindMatchingRules(message, enabledRules);
                                    foreach (var (rule, action) in matchingRules)
                                    {
                                        var replayResult = await replayExecutor.ExecuteAsync(
                                            message, rule, action, stoppingToken);

                                        if (replayResult.IsSuccess)
                                        {
                                            _logger.LogInformation(
                                                "Auto-replay rule {RuleName} replayed message {MessageId}",
                                                Security.LogRedactor.SanitiseForLog(rule.Name),
                                                Security.LogRedactor.SanitiseForLog(message.MessageId));
                                        }

                                        break; // Only apply first matching rule per message
                                    }
                                }
                            }
                        }
                        catch (Exception ruleEx)
                        {
                            _logger.LogWarning(ruleEx,
                                "Error evaluating auto-replay rules for namespace {NamespaceId}", ns.Id);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        // Graceful shutdown
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error scanning namespace {NamespaceId}", ns.Id);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("DLQ Monitor Worker stopping gracefully");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DLQ Monitor Worker poll cycle");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }

        _logger.LogInformation("DLQ Monitor Worker stopped");
    }
}
