using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Periodically purges audit log entries older than the configured retention window
/// (<c>Audit:Retention:*</c>). A separate worker from <see cref="AuditService"/> (which owns
/// the write-channel drain loop) rather than folded into it — the two run on entirely
/// unrelated schedules: writes happen whenever a request occurs, retention sweeps happen on a
/// fixed clock regardless of write activity, so combining them would mean the sweep only gets a
/// chance to run when there happens to be audit traffic to wake the write-drain loop.
/// <para>
/// Disabled by default (<see cref="AuditRetentionOptions.Enabled"/> is <c>false</c>) — audit
/// logs are kept forever unless an operator opts in, so upgrading ServiceHub never silently
/// deletes existing compliance records.
/// </para>
/// </summary>
public sealed class AuditRetentionWorker : BackgroundService
{
    private static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromMinutes(1);

    private readonly IAuditService _auditService;
    private readonly AuditRetentionOptions _options;
    private readonly ILogger<AuditRetentionWorker> _logger;
    private readonly TimeSpan _initialDelay;

    /// <summary>Initializes a new instance of the <see cref="AuditRetentionWorker"/> class.</summary>
    /// <param name="auditService">The audit service, used to run the purge sweep.</param>
    /// <param name="options">Retention options, bound from <c>Audit:Retention</c>.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="initialDelay">
    /// Delay before the first sweep. Defaults to 1 minute (retention isn't latency-sensitive
    /// the way DLQ scanning is, so there's no need to rush it at cold start); overridable for
    /// tests that don't want to wait a full minute.
    /// </param>
    public AuditRetentionWorker(
        IAuditService auditService,
        IOptions<AuditRetentionOptions> options,
        ILogger<AuditRetentionWorker> logger,
        TimeSpan? initialDelay = null)
    {
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _initialDelay = initialDelay ?? DefaultInitialDelay;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Audit Retention Worker starting in disabled mode — audit logs are kept forever. " +
                "Set Audit:Retention:Enabled=true to opt in to automatic purging.");
            return;
        }

        _logger.LogInformation(
            "Audit Retention Worker starting: retaining {RetentionDays} days, sweeping every {SweepIntervalHours}h",
            _options.RetentionDays, _options.SweepIntervalHours);

        try
        {
            await Task.Delay(_initialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var sweepInterval = TimeSpan.FromHours(Math.Max(1, _options.SweepIntervalHours));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, _options.RetentionDays));
                var result = await _auditService.PurgeExpiredAsync(cutoff, stoppingToken);

                if (result.IsFailure)
                {
                    _logger.LogWarning("Audit retention sweep failed: {Error}", result.Error.Message);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during audit retention sweep");
            }

            try
            {
                await Task.Delay(sweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Audit Retention Worker stopped");
    }
}
