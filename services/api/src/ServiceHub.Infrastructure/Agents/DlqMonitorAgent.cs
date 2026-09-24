using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Dlq;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Agents;

/// <summary>
/// Looks in every connected cloud's dead-letter queues and keeps a durable list of what is stuck there
/// (unit 2.1) — the first real agent, and the one everything after it reads.
/// </summary>
/// <remarks>
/// It <b>observes</b>: it reads the clouds and writes only ServiceHub's own table. It never replays,
/// purges or moves a message, and the host treats a reported change as a contract violation.
/// The loop, the cadence, the heartbeat and the error handling belong to <see cref="AgentHost"/>; this
/// class is one cycle.
/// </remarks>
public sealed class DlqMonitorAgent : IAgent
{
    private const int DefaultPollIntervalSeconds = 10;
    private const int DefaultMaxParallelScans = 10;

    private readonly IServiceScopeFactory _scopes;
    private readonly int _maxParallelScans;
    private readonly ILogger<DlqMonitorAgent> _logger;

    /// <summary>Creates the agent. Cadence and parallelism come from the <c>DlqMonitor</c> section.</summary>
    public DlqMonitorAgent(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<DlqMonitorAgent> logger)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        // Clamped, not thrown: a typo in a tuning knob must not stop monitoring, and the bounds keep
        // a value from busy-looping the clouds or exhausting connections.
        var interval = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue("DlqMonitor:PollIntervalSeconds", DefaultPollIntervalSeconds), 1, 3600));
        _maxParallelScans = Math.Clamp(configuration.GetValue("DlqMonitor:MaxParallelScans", DefaultMaxParallelScans), 1, 100);

        Descriptor = new AgentDescriptor(
            Id: "dlq-monitor",
            Name: "Dead-letter Monitor",
            Purpose: "Looks in every connected cloud's dead-letter queues, keeps a lasting list of what is stuck there, and notices when a message is gone.",
            Kind: AgentKind.Watch,
            Authority: AgentAuthority.Observes,
            Cadence: interval,
            Notes: "AWS and Google Cloud are not watched automatically: looking at a message there counts as a delivery attempt and can dead-letter it by accident.");
    }

    /// <inheritdoc />
    public AgentDescriptor Descriptor { get; }

    /// <inheritdoc />
    public async Task<AgentCycleResult> ExecuteCycleAsync(CancellationToken ct)
    {
        List<Core.Entities.Namespace> active;
        int archived;
        using (var scope = _scopes.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<INamespaceRepository>();

            var activeResult = await repository.GetActiveAsync(ct).ConfigureAwait(false);
            if (activeResult.IsFailure)
            {
                throw new InvalidOperationException("The list of connected clouds could not be read.");
            }

            active = [.. activeResult.Value];
            archived = await ArchiveOrphansAsync(scope.ServiceProvider, repository, ct).ConfigureAwait(false);
        }

        if (active.Count == 0)
        {
            return archived > 0
                ? new AgentCycleResult(0, 0, $"no clouds are connected, {Count(archived, "record")} archived because their cloud was removed")
                : AgentCycleResult.Idle("no clouds are connected");
        }

        using var gate = new SemaphoreSlim(_maxParallelScans);
        var scans = await Task.WhenAll(active.Select(ns => ScanOneAsync(ns, gate, ct))).ConfigureAwait(false);

        var scanned = scans.Where(s => s.Result.Outcome == ScanOutcome.Scanned).ToList();
        var skipped = scans.Where(s => s.Result.Outcome == ScanOutcome.Skipped).ToList();
        var failed = scans.Where(s => s.Result.Outcome == ScanOutcome.Failed).ToList();
        var unconfirmed = scanned.Sum(s => s.Result.Unconfirmed);

        // Tell anyone watching that a queue's contents changed — a hint to look again, never the data itself.
        using (var scope = _scopes.CreateScope())
        {
            if (scope.ServiceProvider.GetService<IPlatformEventBus>() is { } bus)
            {
                foreach (var (ns, result) in scanned.Where(s => s.Result.NewMessages > 0 || s.Result.Resolved > 0))
                {
                    await bus.PublishAsync(new PlatformEvent
                    {
                        Source = Descriptor.Id, Category = EventCategories.Dlq, EventType = EventTypes.DlqMessageDetected,
                        CloudProvider = ns.Provider.ToString().ToLowerInvariant(), NamespaceId = ns.Id, NamespaceName = ns.Name, Actor = ns.OwnerId,
                    }, ct).ConfigureAwait(false);
                }
            }
        }

        var parts = new List<string>
        {
            $"looked at {Count(scanned.Sum(s => s.Result.EntitiesExamined), "queue or subscription")} in {Count(scanned.Count, "cloud")}",
            $"{Count(scanned.Sum(s => s.Result.NewMessages), "new dead letter")}",
            $"{Count(scanned.Sum(s => s.Result.Resolved), "gone from the queue")}",
        };
        if (skipped.Count > 0) parts.Add($"{Count(skipped.Count, "cloud")} not watched automatically");
        if (failed.Count > 0) parts.Add($"{Count(failed.Count, "cloud")} could not be read");
        if (unconfirmed > 0) parts.Add($"{Count(unconfirmed, "queue")} could not be confirmed and were left as they were");
        if (archived > 0) parts.Add($"{Count(archived, "record")} archived because their cloud was removed");

        // Recording what was found is this agent's own bookkeeping, not a change to anything outside
        // ServiceHub — so Changed stays 0, as an Observes agent's must.
        return new AgentCycleResult(
            Examined: scanned.Sum(s => s.Result.EntitiesExamined),
            Changed: 0,
            Summary: string.Join(", ", parts),
            Degraded: failed.Count > 0 || unconfirmed > 0);
    }

    private async Task<(Core.Entities.Namespace Namespace, NamespaceScanResult Result)> ScanOneAsync(
        Core.Entities.Namespace ns, SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var scope = _scopes.CreateScope();
            // Built from the scope's own services, so registering this agent stays one line.
            var scanner = ActivatorUtilities.CreateInstance<DlqScanner>(scope.ServiceProvider);
            return (ns, await scanner.ScanAsync(ns, ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // One namespace failing must not lose the others' results.
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scanning namespace {NamespaceId} failed", ns.Id);
            return (ns, new NamespaceScanResult(ScanOutcome.Failed, Reason: "The scan failed."));
        }
#pragma warning restore CA1031
        finally
        {
            gate.Release();
        }
    }

    // Active rows whose cloud is no longer registered can never be scanned or replayed again.
    private async Task<int> ArchiveOrphansAsync(IServiceProvider services, INamespaceRepository repository, CancellationToken ct)
    {
        var all = await repository.GetAllAsync(ct).ConfigureAwait(false);
        if (all.IsFailure)
        {
            return 0;
        }

        var known = all.Value.Select(n => n.Id).ToList();
        var db = services.GetRequiredService<ServiceHubDbContext>();

        // Membership is tested in SQL: pulling every Active row into memory on every cycle would be
        // worst exactly when a large queue is what needs investigating.
        var orphans = await db.DlqMessages
            .Where(m => m.Status == DlqMessageStatus.Active && !known.Contains(m.NamespaceId))
            .ToListAsync(ct).ConfigureAwait(false);
        if (orphans.Count == 0)
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var orphan in orphans)
        {
            orphan.Status = DlqMessageStatus.Archived;
            orphan.ArchivedAt = now;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return orphans.Count;
    }

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? string.Empty : "s")}";
}
