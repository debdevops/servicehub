using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Infrastructure.OAuth;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Background worker that periodically purges expired OAuth sessions and PKCE states
/// from <see cref="InMemoryOAuthStore"/> to prevent unbounded memory growth.
/// Runs every 15 minutes; sessions expire after 8 hours, PKCE states after 10 minutes.
/// </summary>
internal sealed class OAuthCleanupWorker : BackgroundService
{
    private readonly InMemoryOAuthStore _store;
    private readonly ILogger<OAuthCleanupWorker> _logger;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Initializes a new instance of the <see cref="OAuthCleanupWorker"/> class.
    /// </summary>
    /// <param name="store">The OAuth session store to clean up.</param>
    /// <param name="logger">The logger instance.</param>
    public OAuthCleanupWorker(InMemoryOAuthStore store, ILogger<OAuthCleanupWorker> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OAuth cleanup worker starting (interval: {Interval})", _cleanupInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_cleanupInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                _store.PurgeExpired();
                _logger.LogDebug("OAuth store purge completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during OAuth store cleanup");
            }
        }

        _logger.LogInformation("OAuth cleanup worker stopping");
    }
}
