using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Idempotently records a <see cref="Core.Enums.RecoveryEventType.ProductionElevationExpired"/>
/// ledger event for every <see cref="Core.Entities.ProductionElevation"/> that has naturally
/// lapsed past its absolute <see cref="Core.Entities.ProductionElevation.ExpiresAt"/> — ADR-0010
/// §Decision's "for how long" audit completeness. Deliberately a separate, lazy sweep rather than
/// an inline side effect of the Recovery Eligibility Gate's predicate 2 read
/// (<see cref="IRecoveryLedger.GetLiveProductionElevationAsync"/>), which stays a pure read with
/// no write path — mirrors <see cref="PlaybookExpiryWorker"/>'s single-pass shape: an elevation's
/// expiry moment is fixed at approval time, so there is nothing to flag first.
/// </summary>
public sealed class ProductionElevationExpiryWorker : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(40);
    private const int DefaultSweepIntervalSeconds = 300;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProductionElevationExpiryWorker> _logger;
    private readonly IWorkerHeartbeatStore? _heartbeatStore;

    private readonly TimeSpan _sweepInterval;

    /// <summary>Initializes a new instance of the <see cref="ProductionElevationExpiryWorker"/> class.</summary>
    /// <param name="serviceProvider">Root service provider for per-sweep-cycle scope creation.</param>
    /// <param name="configuration">
    /// Application configuration — reads <c>ProductionElevation:ExpirySweepIntervalSeconds</c>
    /// (default 300, clamped to [60, 3600]). Elevations are short by default (a shift, not a
    /// sprint — ADR-0010 §Decision), so this sweeps far more often than
    /// <see cref="PlaybookExpiryWorker"/>'s hourly default.
    /// </param>
    /// <param name="logger">Logger instance.</param>
    public ProductionElevationExpiryWorker(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<ProductionElevationExpiryWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        _sweepInterval = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue("ProductionElevation:ExpirySweepIntervalSeconds", DefaultSweepIntervalSeconds),
            60, 3600));

        _heartbeatStore = serviceProvider.GetService<IWorkerHeartbeatStore>();
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Production Elevation Expiry Worker starting. Sweep interval: {Interval}s", _sweepInterval.TotalSeconds);

        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var namespaceRepo = scope.ServiceProvider.GetRequiredService<INamespaceRepository>();

                var namespacesResult = await namespaceRepo.GetActiveAsync(stoppingToken);
                if (namespacesResult.IsSuccess)
                {
                    var ownerIds = namespacesResult.Value
                        .Select(n => n.OwnerId)
                        .Distinct(StringComparer.Ordinal);

                    foreach (var ownerId in ownerIds)
                    {
                        await SweepOwnerAsync(scope.ServiceProvider, ownerId, stoppingToken);
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Production Elevation Expiry Worker could not list active namespaces: {Error}",
                        namespacesResult.Error.Message);
                }

                _heartbeatStore?.RecordHeartbeat(nameof(ProductionElevationExpiryWorker), _sweepInterval);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Production Elevation Expiry Worker sweep cycle");
            }

            try
            {
                await Task.Delay(_sweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Production Elevation Expiry Worker stopped");
    }

    /// <summary>Sweeps one owner's naturally-lapsed, unrecorded elevations. Internal so tests can
    /// drive a single sweep cycle directly instead of waiting on <see cref="ExecuteAsync"/>'s
    /// timer loop.</summary>
    internal async Task SweepOwnerAsync(IServiceProvider services, string ownerId, CancellationToken cancellationToken)
    {
        var recoveryLedger = services.GetRequiredService<IRecoveryLedger>();

        var due = await recoveryLedger.GetUnrecordedExpiredElevationsAsync(ownerId, cancellationToken);

        foreach (var elevation in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await recoveryLedger.RecordProductionElevationExpiryAsync(elevation.Id, ownerId, cancellationToken);
            if (result.IsFailure)
            {
                _logger.LogDebug(
                    "Skipped recording expiry for production elevation {ElevationId}: {Error}",
                    elevation.Id, result.Error.Message);
            }
            else
            {
                _logger.LogInformation(
                    "Production elevation {ElevationId} for namespace {NamespaceId} lapsed past its expiry",
                    elevation.Id, elevation.NamespaceId);
            }
        }
    }
}
