using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Insights;

/// <summary>One finding as an engine produced it this cycle.</summary>
internal sealed record Noticed(string OwnerId, string Key, Guid? NamespaceId, string? EntityName, int Severity, string What, IReadOnlyDictionary<string, double>? Metrics);

/// <summary>
/// What every Insights agent shares (unit 6.18): run one copied 4.0.0 engine over every connected namespace, then make the
/// findings table match what is true now — new ones added, ones still true refreshed, ones no longer true marked cleared.
/// Watch · Observes: nothing here replays, opens a rule, or changes authority. No language model.
/// </summary>
public abstract class InsightAgent : IAgent
{
    private readonly IServiceScopeFactory _scopes;
    private readonly string _kind;

    /// <summary>Creates the agent. Cadence from <c>Insights:IntervalMinutes</c> (default 15).</summary>
    protected InsightAgent(IServiceScopeFactory scopes, IConfiguration configuration, string kind, string id, string name, string purpose, string[] may)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _kind = kind;
        var minutes = Math.Clamp((configuration ?? throw new ArgumentNullException(nameof(configuration))).GetValue("Insights:IntervalMinutes", 15), 1, 24 * 60);
        Descriptor = new AgentDescriptor(id, name, purpose, AgentKind.Watch, AgentAuthority.Observes, TimeSpan.FromMinutes(minutes),
            Notes: "Counts only what ServiceHub has recorded. A finding is information — it never acts.",
            May: may, MayNot: ["Replay, purge or change anything", "Open a rule or change what a failure may do on its own", "Use AI or guesses"]);
    }

    /// <inheritdoc />
    public AgentDescriptor Descriptor { get; }

    /// <summary>What the engine notices across these namespaces now.</summary>
    internal abstract Task<IReadOnlyList<Noticed>> NoticeAsync(IServiceProvider services, IReadOnlyList<Namespace> namespaces, DateTimeOffset now, CancellationToken ct);

    /// <inheritdoc />
    public async Task<AgentCycleResult> ExecuteCycleAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var namespaces = await scope.ServiceProvider.GetRequiredService<INamespaceRepository>().GetActiveAsync(ct).ConfigureAwait(false);
        if (namespaces.IsFailure)
        {
            throw new InvalidOperationException("The list of connected clouds could not be read.");
        }

        var now = DateTimeOffset.UtcNow;
        var noticed = await NoticeAsync(scope.ServiceProvider, namespaces.Value, now, ct).ConfigureAwait(false);
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        var open = await db.InsightFindings.Where(f => f.Kind == _kind && f.ClearedAt == null).ToListAsync(ct).ConfigureAwait(false);
        var byKey = open.ToDictionary(f => (f.OwnerId, f.Key));
        var added = 0;

        foreach (var n in noticed.GroupBy(n => (n.OwnerId, n.Key)).Select(g => g.OrderByDescending(x => x.Severity).First()))
        {
            var metrics = n.Metrics is null ? null : JsonSerializer.Serialize(n.Metrics);
            if (byKey.Remove((n.OwnerId, n.Key), out var existing))
            {
                existing.LastSeenAt = now;
                existing.Severity = n.Severity;
                existing.What = n.What;
                existing.MetricsJson = metrics;
                continue;
            }

            db.InsightFindings.Add(new InsightFinding
            {
                OwnerId = n.OwnerId, Kind = _kind, Key = n.Key, NamespaceId = n.NamespaceId, EntityName = n.EntityName, Severity = n.Severity,
                What = n.What, MetricsJson = metrics, FirstSeenAt = now, LastSeenAt = now,
            });
            added++;
        }

        foreach (var gone in byKey.Values)
        {
            gone.ClearedAt = now;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new AgentCycleResult(namespaces.Value.Count, added + byKey.Count, $"{noticed.Count} current, {added} new, {byKey.Count} cleared");
    }

    /// <summary>Anomalies over the last day against the four days before it, for every namespace.</summary>
    internal static async Task<List<(Namespace Ns, Anomaly Anomaly)>> AnomaliesAsync(IServiceProvider services, IReadOnlyList<Namespace> namespaces, DateTimeOffset now, CancellationToken ct)
    {
        var engine = services.GetRequiredService<IAnomalyDetectionService>();
        var found = new List<(Namespace, Anomaly)>();
        foreach (var ns in namespaces)
        {
            var result = await engine.DetectAnomaliesAsync(ns.Id, now.AddDays(-1), now, ct).ConfigureAwait(false);
            if (result.IsSuccess) found.AddRange(result.Value.Select(a => (ns, a)));
        }

        return found;
    }
}

/// <summary>Spikes and drops in dead letters per queue — 4.0.0's anomaly engine, unchanged.</summary>
public sealed class AnomalyInsightAgent(IServiceScopeFactory scopes, IConfiguration configuration) : InsightAgent(scopes, configuration,
    "anomaly", "insights-anomaly", "Spike Watcher",
    "Notices when a queue is dead-lettering far more — or far less — than its own last four days.",
    ["Record a spike or drop, with the numbers behind it"])
{
    internal override async Task<IReadOnlyList<Noticed>> NoticeAsync(IServiceProvider services, IReadOnlyList<Namespace> namespaces, DateTimeOffset now, CancellationToken ct) =>
        [.. (await AnomaliesAsync(services, namespaces, now, ct).ConfigureAwait(false))
            .Select(x => new Noticed(x.Ns.OwnerId, $"{x.Ns.Id}|{x.Anomaly.EntityName}|{x.Anomaly.Type}", x.Ns.Id, x.Anomaly.EntityName, x.Anomaly.Severity, x.Anomaly.Description, x.Anomaly.Metrics))];
}

/// <summary>Dead-letter backlogs growing towards a threshold — 4.0.0's backlog forecast, unchanged.</summary>
public sealed class BacklogInsightAgent(IServiceScopeFactory scopes, IConfiguration configuration) : InsightAgent(scopes, configuration,
    "backlog", "insights-backlog", "Backlog Forecaster",
    "Projects when a growing dead-letter backlog will cross its threshold, from how fast it has been growing.",
    ["Record a projection, with the growth rate behind it"])
{
    internal override async Task<IReadOnlyList<Noticed>> NoticeAsync(IServiceProvider services, IReadOnlyList<Namespace> namespaces, DateTimeOffset now, CancellationToken ct)
    {
        var engine = services.GetRequiredService<IBacklogForecastService>();
        var found = new List<Noticed>();
        foreach (var ns in namespaces)
        {
            var result = await engine.ForecastAsync(ns.Id, now.AddHours(-1), now, cancellationToken: ct).ConfigureAwait(false);
            if (result.IsFailure) continue;
            found.AddRange(result.Value.Select(f => new Noticed(ns.OwnerId, $"{ns.Id}|{f.EntityName}", ns.Id, f.EntityName, f.Severity, f.Description, f.Metrics)));
        }

        return found;
    }
}

/// <summary>The same kind of trouble in several places at once — 4.0.0's correlation engine, unchanged.</summary>
public sealed class CorrelationInsightAgent(IServiceScopeFactory scopes, IConfiguration configuration) : InsightAgent(scopes, configuration,
    "correlation", "insights-correlation", "Pattern Linker",
    "Notices when queues in different namespaces or clouds spike together — often one shared cause.",
    ["Record which queues moved together"])
{
    internal override async Task<IReadOnlyList<Noticed>> NoticeAsync(IServiceProvider services, IReadOnlyList<Namespace> namespaces, DateTimeOffset now, CancellationToken ct)
    {
        var observations = (await AnomaliesAsync(services, namespaces, now, ct).ConfigureAwait(false))
            .Select(x => new AnomalyObservation(x.Anomaly, x.Ns.OwnerId, x.Ns.Provider)).ToList();
        return [.. services.GetRequiredService<ICorrelationDetectionService>().DetectCorrelations(observations)
            .Select(c => new Noticed(c.OwnerId, string.Join(",", c.Members.Select(m => $"{m.NamespaceId}|{m.EntityName}").Order(StringComparer.Ordinal)),
                null, null, c.Severity, c.Description, c.Metrics))];
    }
}

/// <summary>Plain-English summaries over the other findings — 4.0.0's templated narration, unchanged. Shown as a suggestion (R3).</summary>
public sealed class NarrationInsightAgent(IServiceScopeFactory scopes, IConfiguration configuration) : InsightAgent(scopes, configuration,
    "narration", "insights-narration", "Narrator",
    "Writes a short, templated summary of what the other Insights agents found — fixed sentences, no AI.",
    ["Write a summary, marked as a suggestion"])
{
    internal override async Task<IReadOnlyList<Noticed>> NoticeAsync(IServiceProvider services, IReadOnlyList<Namespace> namespaces, DateTimeOffset now, CancellationToken ct)
    {
        var anomalies = await AnomaliesAsync(services, namespaces, now, ct).ConfigureAwait(false);
        var correlations = services.GetRequiredService<ICorrelationDetectionService>()
            .DetectCorrelations([.. anomalies.Select(x => new AnomalyObservation(x.Anomaly, x.Ns.OwnerId, x.Ns.Provider))]);
        var owners = namespaces.ToDictionary(n => n.Id, n => n.OwnerId);
        var narrations = services.GetRequiredService<INarrationService>().GenerateNarrations(
            namespaces.ToDictionary(n => n.Id), [.. anomalies.Select(x => x.Anomaly)], [], correlations);
        return [.. narrations
            .Select(n => (n, owner: n.NamespaceId is { } id && owners.TryGetValue(id, out var o) ? o : n.AccessNamespaceIds.Select(a => owners.GetValueOrDefault(a)).FirstOrDefault(x => x is not null)))
            .Where(x => x.owner is not null)
            .Select(x => new Noticed(x.owner!, $"{x.n.Kind}|{x.n.NamespaceId}|{string.Join(",", x.n.AccessNamespaceIds.Order())}", x.n.NamespaceId, null, x.n.Severity,
                $"{x.n.Headline} {x.n.Summary}", null))];
    }
}
