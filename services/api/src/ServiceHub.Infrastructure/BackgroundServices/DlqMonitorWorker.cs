using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Background worker that periodically scans all registered namespace DLQs.
/// Uses adaptive polling: active DLQs are scanned more frequently.
/// </summary>
public sealed class DlqMonitorWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DlqMonitorWorker> _logger;

    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ActivePollInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan InactivePollInterval = TimeSpan.FromMinutes(15);
    private static readonly int MaxParallelScans = 10;

    /// <summary>
    /// Initializes a new instance of the <see cref="DlqMonitorWorker"/> class.
    /// </summary>
    public DlqMonitorWorker(
        IServiceProvider serviceProvider,
        ILogger<DlqMonitorWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        var lastDetectionCounts = new Dictionary<Guid, (int count, DateTimeOffset timestamp)>();

        while (!stoppingToken.IsCancellationRequested)
        {
            var nextInterval = ActivePollInterval;

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var namespaceRepo = scope.ServiceProvider.GetRequiredService<INamespaceRepository>();

                var namespacesResult = await namespaceRepo.GetActiveAsync();
                if (namespacesResult.IsFailure)
                {
                    _logger.LogWarning("Failed to get active namespaces: {Error}", namespacesResult.Error.Message);
                    await Task.Delay(ActivePollInterval, stoppingToken);
                    continue;
                }

                var namespaces = namespacesResult.Value;
                if (namespaces.Count == 0)
                {
                    _logger.LogDebug("No active namespaces found, sleeping for {Interval}s", InactivePollInterval.TotalSeconds);
                    await Task.Delay(InactivePollInterval, stoppingToken);
                    continue;
                }

                _logger.LogDebug("Scanning DLQs for {Count} namespace(s)", namespaces.Count);

                using var semaphore = new SemaphoreSlim(MaxParallelScans);
                var tasks = namespaces.Select(async ns =>
                {
                    await semaphore.WaitAsync(stoppingToken);
                    try
                    {
                        // Adaptive polling: skip inactive namespaces
                        if (lastDetectionCounts.TryGetValue(ns.Id, out var last))
                        {
                            var elapsed = DateTimeOffset.UtcNow - last.timestamp;
                            if (last.count == 0 && elapsed < InactivePollInterval)
                            {
                                _logger.LogDebug(
                                    "Skipping inactive namespace {NamespaceId} (last scan {ElapsedMin:F0}m ago, no messages)",
                                    ns.Id, elapsed.TotalMinutes);
                                return;
                            }
                        }

                        using var innerScope = _serviceProvider.CreateScope();
                        var monitor = innerScope.ServiceProvider.GetRequiredService<IDlqMonitorService>();
                        var result = await monitor.ScanNamespaceAsync(ns.Id, stoppingToken);

                        var detectedCount = result.IsSuccess ? result.Value : 0;
                        lastDetectionCounts[ns.Id] = (detectedCount, DateTimeOffset.UtcNow);
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

                // If any namespace had detections, use active polling interval
                var hasActivity = lastDetectionCounts.Values.Any(v =>
                    v.count > 0 && (DateTimeOffset.UtcNow - v.timestamp) < TimeSpan.FromHours(2));

                nextInterval = hasActivity ? ActivePollInterval : InactivePollInterval;
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

            await Task.Delay(nextInterval, stoppingToken);
        }

        _logger.LogInformation("DLQ Monitor Worker stopped");
    }
}
