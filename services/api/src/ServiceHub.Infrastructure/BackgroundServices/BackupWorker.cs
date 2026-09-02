using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Periodically creates a backup bundle on the configured interval (<c>Backup:*</c>), roadmap F1/F2.
/// <para>
/// Disabled in the base configuration (<see cref="BackupOptions.ScheduledBackupIntervalHours"/> is
/// 0), but on by default in Production (every 24h). An on-demand backup remains available via
/// <c>POST /api/v1/admin/backup</c> regardless of this setting, mirroring
/// <see cref="AuditRetentionWorker"/>'s relationship to <c>POST /api/v1/audit/purge</c>.
/// </para>
/// </summary>
public sealed class BackupWorker : BackgroundService
{
    private static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceProvider _serviceProvider;
    private readonly BackupOptions _options;
    private readonly ILogger<BackupWorker> _logger;
    private readonly TimeSpan _initialDelay;

    /// <summary>Initializes a new instance of the <see cref="BackupWorker"/> class.</summary>
    /// <param name="serviceProvider">Root service provider, used to create a scope per backup
    /// (<see cref="IBackupService"/> is Scoped — it depends on the Scoped <c>DlqDbContext</c>).</param>
    /// <param name="options">Backup options, bound from <c>Backup</c>.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="initialDelay">Delay before the first scheduled backup. Defaults to 1 minute;
    /// overridable for tests that don't want to wait a full minute.</param>
    public BackupWorker(
        IServiceProvider serviceProvider,
        IOptions<BackupOptions> options,
        ILogger<BackupWorker> logger,
        TimeSpan? initialDelay = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _initialDelay = initialDelay ?? DefaultInitialDelay;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.ScheduledBackupIntervalHours <= 0)
        {
            _logger.LogInformation(
                "Backup Worker starting in disabled mode — no scheduled backups. " +
                "Set Backup:ScheduledBackupIntervalHours to a positive value to opt in, or use " +
                "POST /api/v1/admin/backup for an on-demand backup.");
            return;
        }

        _logger.LogInformation(
            "Backup Worker starting: scheduled backup every {IntervalHours}h",
            _options.ScheduledBackupIntervalHours);

        try
        {
            await Task.Delay(_initialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var interval = TimeSpan.FromHours(_options.ScheduledBackupIntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();
                var result = await backupService.CreateBackupAsync(stoppingToken);

                if (result.IsFailure)
                {
                    _logger.LogWarning("Scheduled backup failed: {Error}", result.Error.Message);
                }
                else
                {
                    _logger.LogInformation("Scheduled backup {BackupId} completed", result.Value.BackupId);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during scheduled backup");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Backup Worker stopped");
    }
}
