using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

    // Defaults preserve the previously hardcoded cadence exactly, so an operator who
    // configures nothing sees identical behaviour to earlier releases. Each poll cycle
    // costs one live provider peek per namespace, so on a large fleet (or a metered
    // provider) an operator needs to be able to trade detection latency for API spend —
    // previously there was no lever at all.
    private const int DefaultPollIntervalSeconds = 10;
    private const int DefaultMaxParallelScans = 10;
    private const int DefaultMaxRuleEvaluationBatch = 500;

    private readonly TimeSpan _pollInterval;
    private readonly int _maxParallelScans;
    private readonly int _maxRuleEvaluationBatch;

    // Keyset position of the last Active row evaluated against auto-replay rules, per namespace.
    // Without it the bounded batch always re-read the same oldest N rows: a message that matches
    // no rule stays Active, so a prefix of N non-matching messages starved every later message
    // out of rule evaluation forever. Held in memory rather than persisted — it is a fairness
    // hint, not a correctness record. Losing it on restart only rewinds the sweep to the oldest
    // message, which is exactly the (safe) pre-fix starting point; duplicate replay is prevented
    // by AutoReplayExecutor's optimistic-concurrency claim, never by this cursor.
    private readonly ConcurrentDictionary<Guid, RuleEvaluationCursor> _ruleEvaluationCursors = new();

    private readonly record struct RuleEvaluationCursor(DateTimeOffset DetectedAtUtc, long Id);

    /// <summary>
    /// Initializes a new instance of the <see cref="DlqMonitorWorker"/> class.
    /// </summary>
    /// <param name="serviceProvider">Root service provider for per-scan-cycle scope creation.</param>
    /// <param name="configuration">Application configuration (<c>DlqMonitor</c> section).</param>
    /// <param name="logger">Logger instance.</param>
    public DlqMonitorWorker(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<DlqMonitorWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        // Clamped rather than validated-and-thrown: a typo in an operational tuning knob
        // should not stop DLQ monitoring altogether. The clamp bounds are wide enough to
        // cover every sane deployment and narrow enough to reject a value that would
        // effectively disable monitoring (0s busy-loop) or exhaust provider connections.
        _pollInterval = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue("DlqMonitor:PollIntervalSeconds", DefaultPollIntervalSeconds),
            1, 3600));
        _maxParallelScans = Math.Clamp(
            configuration.GetValue("DlqMonitor:MaxParallelScans", DefaultMaxParallelScans),
            1, 100);
        _maxRuleEvaluationBatch = Math.Clamp(
            configuration.GetValue("DlqMonitor:MaxRuleEvaluationBatch", DefaultMaxRuleEvaluationBatch),
            1, 100_000);

        // IPlatformEventBus is a singleton — resolve once from the root provider.
        // This avoids resolving it from a scoped context on every poll cycle.
        _eventBus = serviceProvider.GetRequiredService<IPlatformEventBus>();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "DLQ Monitor Worker starting. Initial delay: {Delay}s, poll interval: {PollInterval}s, " +
            "max parallel scans: {MaxParallelScans}, rule-evaluation batch: {RuleBatch}",
            InitialDelay.TotalSeconds,
            _pollInterval.TotalSeconds,
            _maxParallelScans,
            _maxRuleEvaluationBatch);

        // Confirm the schema is reachable before entering the poll loop.
        //
        // This deliberately does NOT call EnsureCreatedAsync. Program.cs owns schema creation
        // via Database.MigrateAsync(), and the two strategies are mutually exclusive:
        // EnsureCreated builds the schema directly with no __EFMigrationsHistory rows, so a
        // database it created can never subsequently be migrated. Calling both left the app one
        // swallowed startup migration away from a database that looked fine and could never be
        // upgraded again.
        try
        {
            using var initScope = _serviceProvider.CreateScope();
            var dbContext = initScope.ServiceProvider.GetRequiredService<DlqDbContext>();
            if (!await dbContext.Database.CanConnectAsync(stoppingToken))
            {
                _logger.LogError(
                    "DLQ Intelligence database is not reachable — DLQ monitoring will not start");
                return;
            }

            _logger.LogInformation("DLQ Intelligence database is reachable");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reach the DLQ Intelligence database");
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
                    await Task.Delay(_pollInterval, stoppingToken);
                    continue;
                }

                var namespaces = namespacesResult.Value;

                if (namespaces.Count == 0)
                {
                    _logger.LogInformation("No active namespaces found, sleeping for {Interval}s", _pollInterval.TotalSeconds);
                    await Task.Delay(_pollInterval, stoppingToken);
                    continue;
                }

                // Drop rule-evaluation cursors for namespaces that are no longer active, so the
                // map tracks the live registration list instead of growing for the process lifetime.
                if (!_ruleEvaluationCursors.IsEmpty)
                {
                    var activeIds = namespaces.Select(n => n.Id).ToHashSet();
                    foreach (var staleId in _ruleEvaluationCursors.Keys.Where(id => !activeIds.Contains(id)))
                    {
                        _ruleEvaluationCursors.TryRemove(staleId, out _);
                    }
                }

                _logger.LogInformation("Scanning DLQs for {Count} namespace(s): {Namespaces}",
                    namespaces.Count,
                    string.Join(", ", namespaces.Select(n => $"{LogRedactor.SanitiseForLog(n.Name)} (ID: {n.Id})")));

                // Archive Active DLQ records whose namespace registration no longer exists —
                // they can never be scanned or replayed, and would otherwise keep matching
                // auto-replay rules (and failing) forever.
                try
                {
                    var allNamespacesResult = await namespaceRepo.GetAllAsync(stoppingToken);
                    if (allNamespacesResult.IsSuccess)
                    {
                        var knownIds = allNamespacesResult.Value.Select(n => n.Id).ToList();
                        var reconcileDb = scope.ServiceProvider.GetRequiredService<DlqDbContext>();

                        // The namespace-membership test runs in SQL. Materialising every Active
                        // row first and filtering in memory pulled the whole active DLQ into the
                        // process on every poll cycle — worst precisely during the large-DLQ
                        // incident this product exists to investigate. EF Core translates
                        // Contains over a local collection into an IN (...) predicate, so in the
                        // normal case (no orphans) this now returns zero rows instead of all of them.
                        var orphans = await reconcileDb.DlqMessages
                            .Where(m => m.Status == Core.Enums.DlqMessageStatus.Active
                                        && !knownIds.Contains(m.NamespaceId))
                            .ToListAsync(stoppingToken);
                        if (orphans.Count > 0)
                        {
                            foreach (var orphan in orphans)
                            {
                                orphan.Status = Core.Enums.DlqMessageStatus.Archived;
                                orphan.ArchivedAt = DateTimeOffset.UtcNow;
                            }

                            await reconcileDb.SaveChangesAsync(stoppingToken);
                            _logger.LogInformation(
                                "Archived {Count} orphaned DLQ record(s) belonging to unregistered namespaces",
                                orphans.Count);
                        }
                    }
                }
                catch (Exception reconcileEx) when (reconcileEx is not OperationCanceledException)
                {
                    _logger.LogWarning(reconcileEx, "Failed to archive orphaned DLQ records");
                }

                    using var semaphore = new SemaphoreSlim(_maxParallelScans);
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
                            await EvaluateAutoReplayRulesAsync(innerScope.ServiceProvider, ns, stoppingToken);
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

            await Task.Delay(_pollInterval, stoppingToken);
        }

        _logger.LogInformation("DLQ Monitor Worker stopped");
    }

    /// <summary>
    /// Evaluates enabled auto-replay rules against one bounded page of a namespace's Active DLQ
    /// messages, advancing a per-namespace keyset cursor so successive calls sweep the whole
    /// backlog instead of re-reading the same oldest page.
    /// </summary>
    /// <param name="scopedServices">Service provider for the current scan scope.</param>
    /// <param name="ns">The namespace whose Active DLQ rows are being evaluated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task EvaluateAutoReplayRulesAsync(
        IServiceProvider scopedServices,
        Core.Entities.Namespace ns,
        CancellationToken cancellationToken)
    {
        var ruleEngine = scopedServices.GetRequiredService<IRuleEngine>();
        var replayExecutor = scopedServices.GetRequiredService<IAutoReplayExecutor>();
        var dbContext = scopedServices.GetRequiredService<Persistence.DlqDbContext>();

        var enabledRules = await dbContext.AutoReplayRules
            .Where(r => r.Enabled)
            .ToListAsync(cancellationToken);

        // Safety-by-default guard: auto-replay is blocked in production,
        // mirroring the human-initiated replay guard in MessagesController.
        if (enabledRules.Count == 0 || ns.Environment == Core.Enums.EnvironmentType.Prod)
            return;

        // Bounded per cycle rather than unbounded, so a 50,000-row DLQ bounds peak memory to
        // the batch instead of the whole table. The batch is a moving window, not a fixed
        // prefix: it resumes from the last row evaluated last cycle. A message that matches no
        // rule keeps its Active status, so a fixed prefix would re-read the same non-matching
        // rows forever and never reach anything behind them.
        var query = dbContext.DlqMessages
            .Where(m => m.NamespaceId == ns.Id
                        && m.Status == Core.Enums.DlqMessageStatus.Active);

        if (_ruleEvaluationCursors.TryGetValue(ns.Id, out var cursor))
        {
            // Keyset (not OFFSET) so rows leaving Active mid-sweep cannot shift the window and
            // skip an unevaluated message. Matches the ORDER BY below exactly.
            query = query.Where(m => m.DetectedAtUtc > cursor.DetectedAtUtc
                                     || (m.DetectedAtUtc == cursor.DetectedAtUtc && m.Id > cursor.Id));
        }

        // Oldest-detected first, which matches the grace-period semantics below (a message
        // becomes eligible as it ages) and makes the batch deterministic.
        var activeMessages = await query
            .OrderBy(m => m.DetectedAtUtc)
            .ThenBy(m => m.Id)
            .Take(_maxRuleEvaluationBatch)
            .ToListAsync(cancellationToken);

        if (activeMessages.Count == _maxRuleEvaluationBatch)
        {
            // A full page means there is more behind it — resume there next cycle.
            var last = activeMessages[^1];
            _ruleEvaluationCursors[ns.Id] = new RuleEvaluationCursor(last.DetectedAtUtc, last.Id);
        }
        else
        {
            // Short (or empty) page means the sweep reached the tail. Wrap to the oldest row so
            // messages that stayed Active — and any row inserted behind the cursor — are
            // re-evaluated. Small DLQs never set a cursor at all, so they behave exactly as before.
            _ruleEvaluationCursors.TryRemove(ns.Id, out _);
        }

        foreach (var message in activeMessages)
        {
            var matchingRules = ruleEngine.FindMatchingRules(message, enabledRules);
            foreach (var (rule, action) in matchingRules)
            {
                // Honour the rule's grace period (measured from DLQ detection)
                // so operators can inspect messages before auto-replay fires;
                // the message is retried on a later poll cycle.
                if (action.DelaySeconds > 0 &&
                    DateTimeOffset.UtcNow < message.DetectedAtUtc.AddSeconds(action.DelaySeconds))
                {
                    break;
                }

                var replayResult = await replayExecutor.ExecuteAsync(
                    message, rule, action, cancellationToken);

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
