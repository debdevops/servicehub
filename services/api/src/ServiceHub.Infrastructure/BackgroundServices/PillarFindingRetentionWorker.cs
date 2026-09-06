using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Periodically purges Investigate/Correlate/Prevent pillar findings older than the configured
/// retention window (<c>Pillars:Retention:*</c>) — the durable successor to the six process-local,
/// 24-hour TTL caches this replaces (roadmap next-chapter M1.2, ADR-0009). Modelled directly on
/// <see cref="AuditRetentionWorker"/>: same options shape, same fixed-clock sweep loop.
/// </summary>
/// <remarks>
/// <para>
/// One worker sweeps all six tables (<c>Anomaly</c>, <c>DriftFinding</c>,
/// <c>CorrelationFinding</c>, <c>Narration</c>, <c>BacklogForecast</c>,
/// <c>ExternalSignalCorrelation</c>) in a single cycle, rather than six separate
/// <see cref="BackgroundService"/> instances — the sweep logic is identical per table and the
/// operational behaviour (one clock, one options section) is the same either way.
/// </para>
/// <para>
/// <b>A finding a <see cref="Core.Entities.PlaybookEntry"/> still cites is never pruned</b>,
/// regardless of age — the exact defect this milestone exists to fix: the Playbook Ledger is
/// append-only and permanent, so anything it cites inherits that. Citation is detected the same
/// way <c>EvidenceRefJson</c> is written (see <c>PreventionRuleEvaluationService</c> and the
/// detection workers' own <c>evidenceRefJson</c> construction): each cited id's GUID string form
/// appears verbatim as a JSON value inside some <c>PlaybookEntry.EvidenceRefJson</c> blob. This
/// sweep loads that set of ids once per cycle rather than parsing structured evidence per pillar,
/// since the field name citing an id (<c>DriftFindingId</c>, <c>AnomalyId</c>, …) differs by
/// pillar but the id's own string form does not.
/// </para>
/// </remarks>
public sealed class PillarFindingRetentionWorker : BackgroundService
{
    private static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PillarFindingRetentionOptions _options;
    private readonly ILogger<PillarFindingRetentionWorker> _logger;
    private readonly TimeSpan _initialDelay;
    private readonly IWorkerHeartbeatStore? _heartbeatStore;

    /// <summary>Initializes a new instance of the <see cref="PillarFindingRetentionWorker"/> class.</summary>
    /// <param name="scopeFactory">Used to resolve a scoped <see cref="DlqDbContext"/> per sweep.</param>
    /// <param name="options">Retention options, bound from <c>Pillars:Retention</c>.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="initialDelay">
    /// Delay before the first sweep. Defaults to 1 minute, same rationale as
    /// <see cref="AuditRetentionWorker"/>; overridable for tests that don't want to wait.
    /// </param>
    /// <param name="heartbeatStore">Self-observability sink (roadmap §6 item 4). Optional, like
    /// <paramref name="initialDelay"/>.</param>
    public PillarFindingRetentionWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<PillarFindingRetentionOptions> options,
        ILogger<PillarFindingRetentionWorker> logger,
        TimeSpan? initialDelay = null,
        IWorkerHeartbeatStore? heartbeatStore = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _initialDelay = initialDelay ?? DefaultInitialDelay;
        _heartbeatStore = heartbeatStore;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Pillar Finding Retention Worker starting in disabled mode (Pillars:Retention:Enabled=false) " +
                "— findings are kept forever until re-enabled.");

            _heartbeatStore?.RecordHeartbeat(nameof(PillarFindingRetentionWorker), expectedInterval: null);
            return;
        }

        _logger.LogInformation(
            "Pillar Finding Retention Worker starting: retaining {RetentionDays} days, sweeping every {SweepIntervalHours}h",
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
                await SweepAsync(cutoff, stoppingToken).ConfigureAwait(false);

                _heartbeatStore?.RecordHeartbeat(nameof(PillarFindingRetentionWorker), sweepInterval);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during pillar finding retention sweep");
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

        _logger.LogInformation("Pillar Finding Retention Worker stopped");
    }

    /// <summary>
    /// Runs one sweep cycle across all six pillar-finding tables, returning the count deleted per
    /// table (present only when non-zero). Internal (not private) so tests can invoke a single
    /// cycle deterministically instead of waiting on the timer loop.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<string, int>> SweepAsync(
        DlqDbContext dbContext, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        // Loaded once per cycle: every EvidenceRefJson blob currently in the Playbook Ledger.
        // Small and cheap relative to the sweep's own cadence (hours, not requests) — see this
        // class's own remarks for why a substring match against the raw JSON is the right
        // mechanism rather than parsing each pillar's differently-shaped evidence payload.
        var evidenceBlobs = await dbContext.PlaybookEntries
            .Select(p => p.EvidenceRefJson)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var deleted = new Dictionary<string, int>
        {
            ["Anomalies"] = await PurgeUncitedAsync(
                dbContext.Anomalies.Where(a => a.DetectedAt < cutoff).Select(a => a.Id),
                dbContext.Anomalies, evidenceBlobs, cancellationToken).ConfigureAwait(false),
            ["DriftFindings"] = await PurgeUncitedAsync(
                dbContext.DriftFindings.Where(f => f.DetectedAt < cutoff).Select(f => f.Id),
                dbContext.DriftFindings, evidenceBlobs, cancellationToken).ConfigureAwait(false),
            ["CorrelationFindings"] = await PurgeUncitedAsync(
                dbContext.CorrelationFindings.Where(f => f.DetectedAt < cutoff).Select(f => f.Id),
                dbContext.CorrelationFindings, evidenceBlobs, cancellationToken).ConfigureAwait(false),
            ["Narrations"] = await PurgeUncitedAsync(
                dbContext.Narrations.Where(n => n.GeneratedAt < cutoff).Select(n => n.Id),
                dbContext.Narrations, evidenceBlobs, cancellationToken).ConfigureAwait(false),
            ["BacklogForecasts"] = await PurgeUncitedAsync(
                dbContext.BacklogForecasts.Where(f => f.DetectedAt < cutoff).Select(f => f.Id),
                dbContext.BacklogForecasts, evidenceBlobs, cancellationToken).ConfigureAwait(false),
            ["ExternalSignalCorrelations"] = await PurgeUncitedAsync(
                dbContext.ExternalSignalCorrelations.Where(c => c.DetectedAt < cutoff).Select(c => c.Id),
                dbContext.ExternalSignalCorrelations, evidenceBlobs, cancellationToken).ConfigureAwait(false),
        };

        return deleted.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private async Task SweepAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DlqDbContext>();
        var deleted = await SweepAsync(dbContext, cutoff, cancellationToken).ConfigureAwait(false);

        if (deleted.Count > 0)
        {
            var breakdown = string.Join(", ", deleted.Select(kv => $"{kv.Key}={kv.Value}"));
            _logger.LogInformation(
                "Pillar finding retention sweep: deleted entries older than {Cutoff:O} ({Breakdown})",
                cutoff, breakdown);
        }
    }

    /// <summary>
    /// Deletes every id in <paramref name="expiredIds"/> except those whose GUID string form
    /// appears in one of <paramref name="evidenceBlobs"/> — the citation exception this worker
    /// exists to enforce.
    /// </summary>
    private static async Task<int> PurgeUncitedAsync<TEntity>(
        IQueryable<Guid> expiredIds,
        DbSet<TEntity> set,
        IReadOnlyList<string> evidenceBlobs,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var candidates = await expiredIds.ToListAsync(cancellationToken).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return 0;
        }

        var toDelete = candidates
            .Where(id => !IsCited(id, evidenceBlobs))
            .ToList();

        if (toDelete.Count == 0)
        {
            return 0;
        }

        return await set
            .Where(BuildIdInPredicate<TEntity>(toDelete))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsCited(Guid id, IReadOnlyList<string> evidenceBlobs)
    {
        if (evidenceBlobs.Count == 0)
        {
            return false;
        }

        var idString = id.ToString();
        foreach (var blob in evidenceBlobs)
        {
            if (blob.Contains(idString, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static System.Linq.Expressions.Expression<Func<TEntity, bool>> BuildIdInPredicate<TEntity>(
        IReadOnlyCollection<Guid> ids)
        where TEntity : class
    {
        // Every one of the six entities exposes "Id" as its primary key property, but they don't
        // share a common interface for it (Anomaly/DriftFinding/etc. predate this worker and
        // aren't worth retrofitting an IHasId onto just for this). Built once per call via
        // reflection over the property, not per row.
        var parameter = System.Linq.Expressions.Expression.Parameter(typeof(TEntity), "e");
        var idProperty = System.Linq.Expressions.Expression.Property(parameter, "Id");
        var idsConstant = System.Linq.Expressions.Expression.Constant(ids);
        var containsCall = System.Linq.Expressions.Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            [typeof(Guid)],
            idsConstant,
            idProperty);

        return System.Linq.Expressions.Expression.Lambda<Func<TEntity, bool>>(containsCall, parameter);
    }
}
