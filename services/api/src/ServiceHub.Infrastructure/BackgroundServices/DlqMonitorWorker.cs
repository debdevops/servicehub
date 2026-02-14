using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Background worker that periodically scans all registered namespace DLQs.
/// Polls active namespaces on a fixed interval.
/// </summary>
public sealed class DlqMonitorWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DlqMonitorWorker> _logger;

    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(5);  // Fast startup
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);  // Aggressive polling for near-realtime DLQ detection
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

                var namespaces = namespacesResult.Value;
                if (namespaces.Count == 0)
                {
                    _logger.LogInformation("No active namespaces found, sleeping for {Interval}s", PollInterval.TotalSeconds);
                    await Task.Delay(PollInterval, stoppingToken);
                    continue;
                }

                _logger.LogInformation("Scanning DLQs for {Count} namespace(s): {Namespaces}", 
                    namespaces.Count, 
                    string.Join(", ", namespaces.Select(n => $"{n.Name} (ID: {n.Id})")));

                using var semaphore = new SemaphoreSlim(MaxParallelScans);
                var tasks = namespaces.Select(async ns =>
                {
                    await semaphore.WaitAsync(stoppingToken);
                    try
                    {
                        using var innerScope = _serviceProvider.CreateScope();
                        var monitor = innerScope.ServiceProvider.GetRequiredService<IDlqMonitorService>();
                        await monitor.ScanNamespaceAsync(ns.Id, stoppingToken);
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
