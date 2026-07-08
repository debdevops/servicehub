using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure;

/// <summary>
/// Implements the persistent audit trail service.
/// Writes are fire-and-forget: entries are posted to an in-process channel and
/// flushed to SQLite by a dedicated background loop, keeping API request threads
/// free from database I/O contention.
/// </summary>
public sealed class AuditService : BackgroundService, IAuditService
{
    private const int ChannelCapacity = 4096;

    private readonly Channel<AuditLog> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditService> _logger;

    public AuditService(IServiceScopeFactory scopeFactory, ILogger<AuditService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _channel = Channel.CreateBounded<AuditLog>(new BoundedChannelOptions(ChannelCapacity)
        {
            // Drop the oldest entry rather than block callers if the channel is full.
            // This matches the "never block a request thread" design rule.
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    // ─── IAuditService.Enqueue ───────────────────────────────────────────────

    /// <inheritdoc />
    public void Enqueue(AuditLog entry)
    {
        // TryWrite is non-blocking. If it fails the channel is draining and we
        // simply discard — audit logging must never throw or block callers.
        if (!_channel.Writer.TryWrite(entry))
        {
            _logger.LogWarning("Audit channel is full — dropping audit entry for action {Action}", entry.Action);
        }
    }

    // ─── BackgroundService ────────────────────────────────────────────────────

    /// <summary>
    /// Drains the audit channel and flushes entries to the SQLite database.
    /// Batches up to 50 entries per commit cycle to reduce write amplification.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AuditService background writer started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wait until at least one entry is available
                await _channel.Reader.WaitToReadAsync(stoppingToken);

                var batch = new List<AuditLog>(50);

                // Drain up to 50 items per batch
                while (batch.Count < 50 && _channel.Reader.TryRead(out var entry))
                {
                    batch.Add(entry);
                }

                if (batch.Count == 0)
                    continue;

                await PersistBatchAsync(batch, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing audit log batch");
                // Brief delay before retrying to avoid tight spin on persistent errors.
                // Cancellation must not escape this catch, or shutdown would skip the
                // final drain below and drop queued entries.
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        // Graceful shutdown: flush remaining entries
        await DrainRemainingAsync();
        _logger.LogInformation("AuditService background writer stopped");
    }

    private async Task PersistBatchAsync(List<AuditLog> batch, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DlqDbContext>();

        db.AuditLogs.AddRange(batch);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Audit batch committed: {Count} entries", batch.Count);
    }

    private async Task DrainRemainingAsync()
    {
        _channel.Writer.TryComplete();

        var remaining = new List<AuditLog>(128);
        while (_channel.Reader.TryRead(out var entry))
            remaining.Add(entry);

        if (remaining.Count == 0)
            return;

        try
        {
            await PersistBatchAsync(remaining, CancellationToken.None);
            _logger.LogInformation("Flushed {Count} remaining audit entries on shutdown", remaining.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush remaining {Count} audit entries on shutdown", remaining.Count);
        }
    }

    // ─── IAuditService.GetLogsAsync ──────────────────────────────────────────

    /// <inheritdoc />
    public async Task<Result<AuditPageResult>> GetLogsAsync(
        string ownerId,
        Guid? namespaceId = null,
        string? search = null,
        string? actionType = null,
        string? outcome = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DlqDbContext>();

        var query = db.AuditLogs
            .AsNoTracking()
            .Where(l => l.OwnerId == ownerId);

        if (namespaceId.HasValue)
            query = query.Where(l => l.NamespaceId == namespaceId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(l =>
                l.UserIdentity.Contains(s) ||
                l.Action.Contains(s) ||
                (l.DetailsJson != null && l.DetailsJson.Contains(s)) ||
                (l.ResourceName != null && l.ResourceName.Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(actionType))
            query = query.Where(l => l.Action.StartsWith(actionType));

        if (!string.IsNullOrWhiteSpace(outcome))
            query = query.Where(l => l.Outcome == outcome);

        if (from.HasValue)
            query = query.Where(l => l.Timestamp >= from.Value);

        if (to.HasValue)
            query = query.Where(l => l.Timestamp <= to.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(l => l.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Result<AuditPageResult>.Success(new AuditPageResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        });
    }

    // ─── IAuditService.GetSummaryAsync ───────────────────────────────────────

    /// <inheritdoc />
    public async Task<Result<AuditSummary>> GetSummaryAsync(
        string ownerId,
        Guid? namespaceId = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DlqDbContext>();

        var query = db.AuditLogs
            .AsNoTracking()
            .Where(l => l.OwnerId == ownerId);

        if (namespaceId.HasValue)
            query = query.Where(l => l.NamespaceId == namespaceId);

        var entries = await query
            .Select(l => new { l.Outcome, l.UserIdentity })
            .ToListAsync(cancellationToken);

        var total = entries.Count;
        var success = entries.Count(e => e.Outcome == "Success");
        var failure = entries.Count(e => e.Outcome == "Failure");
        var partial = entries.Count(e => e.Outcome == "Partial");
        var activeUsers = entries.Select(e => e.UserIdentity).Distinct().Count();

        return Result<AuditSummary>.Success(new AuditSummary
        {
            TotalEvents = total,
            SuccessCount = success,
            FailureCount = failure,
            PartialCount = partial,
            ActiveUsers = activeUsers,
        });
    }

    // ─── IAuditService.ExportAsync ───────────────────────────────────────────

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<AuditLog>>> ExportAsync(
        string ownerId,
        Guid? namespaceId = null,
        string? actionType = null,
        string? outcome = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DlqDbContext>();

        var query = db.AuditLogs
            .AsNoTracking()
            .Where(l => l.OwnerId == ownerId);

        if (namespaceId.HasValue)
            query = query.Where(l => l.NamespaceId == namespaceId);

        if (!string.IsNullOrWhiteSpace(actionType))
            query = query.Where(l => l.Action.StartsWith(actionType));

        if (!string.IsNullOrWhiteSpace(outcome))
            query = query.Where(l => l.Outcome == outcome);

        if (from.HasValue)
            query = query.Where(l => l.Timestamp >= from.Value);

        if (to.HasValue)
            query = query.Where(l => l.Timestamp <= to.Value);

        var results = await query
            .OrderByDescending(l => l.Timestamp)
            .Take(10_000) // Guard against accidental memory exhaustion on large datasets
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<AuditLog>>.Success(results);
    }
}
