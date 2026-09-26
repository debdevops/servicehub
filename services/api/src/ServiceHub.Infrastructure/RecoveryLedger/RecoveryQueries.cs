using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.RecoveryLedger;

/// <summary>
/// The read side of the ledger (units 2.12–2.13). Everything Simple and Advanced say about "how did recoveries
/// end?" comes from <see cref="SummariseAsync"/> — computed here, once, never in the browser.
/// </summary>
/// <remarks>
/// Read-only: nothing here can change a row. <b>Recovered and Unverified are never merged (V1)</b>, the rate has
/// <c>Unverified</c> on neither side, and a rate with nothing to divide is null rather than 0.
/// </remarks>
public sealed class RecoveryQueries : IRecoveryQueries
{
    private const int MaxPageSize = 100;

    private readonly ServiceHubDbContext _db;

    /// <summary>Creates the queries.</summary>
    public RecoveryQueries(ServiceHubDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

    /// <inheritdoc />
    public async Task<RecoverySummary> SummariseAsync(RecoveryScope scope, string window, CancellationToken cancellationToken)
    {
        var rows = await Scoped(scope, window)
            .Select(e => new { e.State, e.ProviderSnapshot, e.VerificationConfidence })
            .ToListAsync(cancellationToken);

        var providers = rows
            .Where(r => r.ProviderSnapshot is not null)
            .GroupBy(r => r.ProviderSnapshot!.Value)
            .OrderBy(g => g.Key)
            .Select(g => new RecoveryProviderSummary(
                g.Key.ToString().ToLowerInvariant(), g.Count(), CountStates(g.Select(r => r.State)), Rate(g.Select(r => r.State))))
            .ToList();

        var returned = rows.Where(r => r.State == RecoveryEntryState.Returned).ToList();
        return new RecoverySummary(
            window, rows.Count, CountStates(rows.Select(r => r.State)), providers, Rate(rows.Select(r => r.State)),
            new RecoveryConfidenceCounts(
                returned.Count(r => r.VerificationConfidence == VerificationConfidence.Exact),
                returned.Count(r => r.VerificationConfidence == VerificationConfidence.Heuristic)),
            rows.Count(r => r.State is RecoveryEntryState.Observing or RecoveryEntryState.Recovered or RecoveryEntryState.Returned or RecoveryEntryState.Unverified));
    }

    /// <inheritdoc />
    public async Task<RecoveryEntryPage> ListAsync(
        RecoveryScope scope, string window, RecoveryEntryState? state, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = Scoped(scope, window);
        if (state is { } wanted)
        {
            query = query.Where(e => e.State == wanted);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(e => e.BegunAt).ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        var operations = await LoadOperationsAsync(rows.Select(r => r.OperationId), cancellationToken);
        return new RecoveryEntryPage([.. rows.Select(r => ToItem(r, operations))], total, page, pageSize);
    }

    /// <inheritdoc />
    public async Task<RecoveryEntryDetail?> GetAsync(RecoveryScope scope, Guid entryId, CancellationToken cancellationToken)
    {
        var entry = await Scoped(scope, "all").FirstOrDefaultAsync(e => e.Id == entryId, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        var operations = await LoadOperationsAsync([entry.OperationId], cancellationToken);
        var events = await _db.RecoveryEvents.AsNoTracking()
            .Where(e => e.OwnerId == entry.OwnerId && e.EntryId == entry.Id)
            .OrderBy(e => e.Seq).ToListAsync(cancellationToken);

        return new RecoveryEntryDetail(
            ToItem(entry, operations), entry.RecoveryMarker, entry.MarkerApplied, entry.DeadLetterReasonSnapshot,
            entry.VerificationResult?.ToString(), entry.ObservationWindowEndsAt, entry.LastEventSeq,
            [.. events.Select(e => new RecoveryEventItem(
                e.Seq, e.EventType.ToString(), e.OccurredAt, ActorOf(e.ActorIdentity), e.DetailJson, e.PrevHash, e.EntryHash))]);
    }

    private IQueryable<RecoveryLedgerEntry> Scoped(RecoveryScope scope, string window)
    {
        var query = _db.RecoveryLedgerEntries.AsNoTracking().Where(e => e.OwnerId == scope.OwnerId);

        // A restricted credential sees only its own namespaces' entries; an unrestricted owner sees all of theirs,
        // including those of a namespace since removed — the evidence outlives the connection.
        if (scope.AllowedNamespaceIds is { } allowed)
        {
            var ids = allowed.Select(id => (Guid?)id).ToList();
            query = query.Where(e => ids.Contains(e.NamespaceId));
        }

        if (scope.NamespaceId is { } ns)
        {
            query = query.Where(e => e.NamespaceId == ns);
        }

        if (scope.Provider is { } provider)
        {
            query = query.Where(e => e.ProviderSnapshot == provider);
        }

        // The environment the namespace was in when the recovery began — so the evidence stays under the label it was made under.
        if (scope.Environment is { } environment)
        {
            query = query.Where(e => e.EnvironmentSnapshot == environment);
        }

        if (SinceOf(window) is { } since)
        {
            query = query.Where(e => e.BegunAt >= since);
        }

        return query;
    }

    /// <summary>The start of a window, or null for all time. An unknown window is treated as 24h — never as "everything".</summary>
    public static DateTimeOffset? SinceOf(string window) => window switch
    {
        "all" => null,
        "7d" => DateTimeOffset.UtcNow.AddDays(-7),
        "30d" => DateTimeOffset.UtcNow.AddDays(-30),
        _ => DateTimeOffset.UtcNow.AddHours(-24),
    };

    /// <summary>Whether a window name is one of the four this API knows.</summary>
    public static bool IsKnownWindow(string window) => window is "24h" or "7d" or "30d" or "all";

    private static IReadOnlyList<RecoveryStateCount> CountStates(IEnumerable<RecoveryEntryState> states)
    {
        var counts = states.GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count());
        return [.. Enum.GetValues<RecoveryEntryState>().Select(s => new RecoveryStateCount(s.ToString(), counts.GetValueOrDefault(s)))];
    }

    /// <summary>Recovered / (Recovered + Returned). Unverified is on neither side; nothing to divide is null, not 0.</summary>
    private static double? Rate(IEnumerable<RecoveryEntryState> states)
    {
        var list = states.ToList();
        var recovered = list.Count(s => s == RecoveryEntryState.Recovered);
        var returned = list.Count(s => s == RecoveryEntryState.Returned);
        return recovered + returned == 0 ? null : (double)recovered / (recovered + returned);
    }

    private async Task<Dictionary<Guid, RecoveryOperation>> LoadOperationsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.Distinct().ToList();
        return await _db.RecoveryOperations.AsNoTracking().Where(o => wanted.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
    }

    private static RecoveryEntryListItem ToItem(RecoveryLedgerEntry e, Dictionary<Guid, RecoveryOperation> operations)
    {
        operations.TryGetValue(e.OperationId, out var op);
        return new RecoveryEntryListItem(
            e.Id, e.OperationId, e.BegunAt, op?.Kind.ToString() ?? "Replay", e.EntityNameSnapshot ?? e.TargetEntity, e.TargetEntity,
            e.ProviderSnapshot?.ToString().ToLowerInvariant(), e.NamespaceNameSnapshot, ActorOf(op?.ActorIdentity ?? "unknown"),
            e.State.ToString(), e.VerificationConfidence?.ToString(), e.DlqMessageId, e.ClosedAt);
    }

    private static ReplayActor ActorOf(string identity)
    {
        var kind = identity.StartsWith("ApiKey:", StringComparison.Ordinal) ? "apiKey"
            : identity.StartsWith("System:", StringComparison.Ordinal) ? "system"
            : "user";
        return new ReplayActor(identity, kind, RecoveryActorLabel.For(identity), RecoveryActorLabel.IsSession(identity));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecoveryEvent>> EventsByActorAsync(string ownerId, string actor, int limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var events = _db.RecoveryEvents.AsNoTracking().Where(e => e.OwnerId == ownerId);
        events = actor.EndsWith(':')
            ? events.Where(e => e.ActorIdentity.StartsWith(actor))
            : events.Where(e => e.ActorIdentity == actor);
        return await events.OrderByDescending(e => e.Seq).Take(Math.Clamp(limit, 1, 200)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}
