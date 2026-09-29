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
        RecoveryScope scope, string window, RecoveryEntryState? state, int page, int pageSize, CancellationToken cancellationToken, RecoveryListFilter? filter = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = Scoped(scope, window);
        if (state is { } wanted)
        {
            query = query.Where(e => e.State == wanted);
        }

        if (filter is not null)
        {
            if (filter.By == "autonomous")
            {
                query = query.Where(e => _db.RecoveryOperations.Any(o => o.Id == e.OperationId && o.ActorIdentity.StartsWith("System:")));
            }
            else if (filter.By == "people")
            {
                query = query.Where(e => _db.RecoveryOperations.Any(o => o.Id == e.OperationId && !o.ActorIdentity.StartsWith("System:")));
            }

            if (!string.IsNullOrWhiteSpace(filter.Entity))
            {
                var entity = filter.Entity.Trim();
                query = query.Where(e => e.EntityNameSnapshot == entity || e.TargetEntity == entity);
            }

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var like = $"%{filter.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
                query = query.Where(e =>
                    EF.Functions.Like(e.EntityNameSnapshot ?? e.TargetEntity, like, "\\") || EF.Functions.Like(e.NamespaceNameSnapshot ?? "", like, "\\")
                    || EF.Functions.Like(e.DeadLetterReasonSnapshot ?? "", like, "\\")
                    || _db.RecoveryOperations.Any(o => o.Id == e.OperationId && EF.Functions.Like(o.ActorIdentity, like, "\\")));
            }
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(e => e.BegunAt).ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        var operations = await LoadOperationsAsync(rows.Select(r => r.OperationId), cancellationToken);
        var levels = await LevelsAsync(scope.OwnerId, rows, cancellationToken);
        return new RecoveryEntryPage([.. rows.Select(r => ToItem(r, operations) with { Level = levels.GetValueOrDefault(r.Id) })], total, page, pageSize);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> DescribeSignaturesAsync(string ownerId, IReadOnlyCollection<string> hashes, CancellationToken cancellationToken)
    {
        if (hashes.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        var rows = await _db.DlqMessages.AsNoTracking()
            .Where(m => m.OwnerId == ownerId && m.SignatureHash != null && hashes.Contains(m.SignatureHash))
            .Select(m => new { Hash = m.SignatureHash!, m.DeadLetterReason, m.EntityName, m.CloudProvider })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.Hash).ToDictionary(
            g => g.Key,
            g => { var r = g.First(); return $"{r.DeadLetterReason ?? "An unrecorded reason"} on {r.EntityName} ({r.CloudProvider})"; });
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
                e.Seq, e.EventType.ToString(), e.OccurredAt, ActorOf(e.ActorIdentity), e.DetailJson, e.PrevHash, e.EntryHash))],
            // Why a person did it — a purge always carries one (unit 6.15).
            operations.TryGetValue(entry.OperationId, out var op) ? op.Reason : null);
    }

    /// <summary>
    /// The autonomy level each entry's signature held when the entry began — read from the recorded promotions and demotions, not
    /// stored on the entry. A signature that has never moved was at the floor (<c>approve</c>); an entry with no signature has none.
    /// </summary>
    private async Task<Dictionary<Guid, string>> LevelsAsync(string ownerId, IReadOnlyList<RecoveryLedgerEntry> rows, CancellationToken ct)
    {
        var hashes = rows.Where(r => r.SignatureHashSnapshot is not null).Select(r => r.SignatureHashSnapshot!).Distinct().ToList();
        var result = new Dictionary<Guid, string>();
        if (hashes.Count == 0)
        {
            return result;
        }

        var events = await _db.RecoveryEvents.AsNoTracking()
            .Where(e => e.OwnerId == ownerId && (e.EventType == RecoveryEventType.AutonomyGrantPromoted || e.EventType == RecoveryEventType.AutonomyGrantDemoted))
            .OrderBy(e => e.Seq).Select(e => new { e.OccurredAt, e.DetailJson }).ToListAsync(ct);
        var moves = new Dictionary<string, List<(DateTimeOffset At, string Level)>>();
        foreach (var e in events)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(e.DetailJson ?? "{}");
                if (doc.RootElement.TryGetProperty("signatureHash", out var h) && h.GetString() is { } hash && hashes.Contains(hash)
                    && doc.RootElement.TryGetProperty("newLevel", out var l) && l.GetString() is { } level)
                {
                    if (!moves.TryGetValue(hash, out var list)) moves[hash] = list = [];
                    list.Add((e.OccurredAt, level.ToLowerInvariant()));
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // A detail that is not JSON says nothing about a level.
            }
        }

        foreach (var r in rows.Where(r => r.SignatureHashSnapshot is not null))
        {
            var level = "approve";
            if (moves.TryGetValue(r.SignatureHashSnapshot!, out var list))
            {
                foreach (var m in list.Where(m => m.At <= r.BegunAt)) level = m.Level;
            }

            result[r.Id] = level;
        }

        return result;
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
            e.State.ToString(), e.VerificationConfidence?.ToString(), e.DlqMessageId, e.ClosedAt, e.EntityTypeSnapshot, MessageId: e.SourceMessageIdSnapshot);
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecoveryEvent>> ChainAsync(string ownerId, CancellationToken cancellationToken) =>
        await _db.RecoveryEvents.AsNoTracking().Where(e => e.OwnerId == ownerId).OrderBy(e => e.Seq).ToListAsync(cancellationToken).ConfigureAwait(false);
}
