using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Signatures;

/// <summary>
/// Reads failure signatures (unit 3.8) from what ServiceHub recorded: the messages carrying each hash and how their replays
/// ended in the ledger. Nothing is invented — no clustering, no category guess — and no autonomy level is shown (unit 4.1 owns those).
/// </summary>
public sealed class SignaturesService : ISignaturesService
{
    private readonly ServiceHubDbContext _db;
    private readonly TimeProvider _time;

    /// <summary>Creates the service.</summary>
    public SignaturesService(ServiceHubDbContext db, TimeProvider? time = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<SignaturePage> ListAsync(
        string ownerId, IReadOnlySet<Guid>? allowed, CloudProviderType? provider, int days, string? tab, string sort, int page, int pageSize, CancellationToken ct)
    {
        var all = await BuildAsync(ownerId, allowed, provider, Math.Clamp(days, 1, 30), ct).ConfigureAwait(false);
        IEnumerable<SignatureSummary> shown = tab switch
        {
            "growing" => all.Where(s => s.Growing),
            "helps" => all.Where(s => s.ReplayVerdict == "helps"),
            "doesnt" => all.Where(s => s.ReplayVerdict == "doesnt"),
            _ => all,
        };
        shown = sort switch
        {
            "recent" => shown.OrderByDescending(s => s.LastSeenAt),
            _ => shown.OrderByDescending(s => s.Messages).ThenByDescending(s => s.LastSeenAt),
        };

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var list = shown.ToList();
        return new SignaturePage(
            [.. list.Skip((page - 1) * pageSize).Take(pageSize)], list.Count, page, pageSize,
            all.Count, all.Count(s => s.Growing), all.Count(s => s.ReplayVerdict == "helps"), all.Count(s => s.ReplayVerdict == "doesnt"));
    }

    /// <inheritdoc />
    public async Task<SignatureSummary?> GetAsync(string ownerId, IReadOnlySet<Guid>? allowed, string hash, CloudProviderType provider, int days, CancellationToken ct) =>
        (await BuildAsync(ownerId, allowed, provider, Math.Clamp(days, 1, 30), ct).ConfigureAwait(false)).FirstOrDefault(s => s.SignatureHash == hash);

    private async Task<List<SignatureSummary>> BuildAsync(string ownerId, IReadOnlySet<Guid>? allowed, CloudProviderType? provider, int days, CancellationToken ct)
    {
        var namespaceIds = await _db.Namespaces.AsNoTracking().Where(n => n.OwnerId == ownerId && (provider == null || n.Provider == provider))
            .Select(n => n.Id).ToListAsync(ct).ConfigureAwait(false);
        if (allowed is not null)
        {
            namespaceIds = [.. namespaceIds.Where(allowed.Contains)];
        }

        var messages = await _db.DlqMessages.AsNoTracking()
            .Where(m => m.OwnerId == ownerId && m.SignatureHash != null && namespaceIds.Contains(m.NamespaceId))
            .Select(m => new { m.Id, m.SignatureHash, m.CloudProvider, m.EntityName, m.DetectedAtUtc, m.Status, m.DeadLetterReason, m.DeadLetterErrorDescription })
            .ToListAsync(ct).ConfigureAwait(false);

        var byMessage = messages.ToDictionary(m => m.Id);
        var replays = await (from h in _db.ReplayHistories.AsNoTracking().Where(h => h.OwnerId == ownerId && h.RecoveryEntryId != null)
                             join e in _db.RecoveryLedgerEntries.AsNoTracking() on h.RecoveryEntryId equals e.Id
                             select new { h.DlqMessageId, e.State }).ToListAsync(ct).ConfigureAwait(false);

        var now = _time.GetUtcNow();
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var result = new List<SignatureSummary>();
        foreach (var g in messages.GroupBy(m => (m.SignatureHash!, m.CloudProvider)))
        {
            var rows = g.ToList();
            var daily = Enumerable.Range(0, days).Select(i => today.AddDays(i - (days - 1)))
                .Select(d => rows.Count(r => r.DetectedAtUtc >= d && r.DetectedAtUtc < d.AddDays(1))).ToList();
            var mine = replays.Where(r => byMessage.TryGetValue(r.DlqMessageId, out var m) && m.SignatureHash == g.Key.Item1 && m.CloudProvider == g.Key.CloudProvider).ToList();
            var fixedCount = mine.Count(r => r.State == RecoveryEntryState.Recovered);
            var returned = mine.Count(r => r.State == RecoveryEntryState.Returned);
            var verified = fixedCount + returned;
            var verdict = verified == 0 ? "unknown" : (double)fixedCount / verified >= 0.5 ? "helps" : "doesnt";

            var half = Math.Max(1, days / 2);
            var recent = daily.TakeLast(half).Sum();
            var earlier = daily.Take(days - half).Sum();
            var first = rows.OrderBy(r => r.DetectedAtUtc).First();
            var newest = rows.OrderByDescending(r => r.DetectedAtUtc).First();
            result.Add(new SignatureSummary(
                g.Key.Item1, g.Key.CloudProvider, first.DeadLetterReason ?? "Unknown", first.DeadLetterErrorDescription is { Length: > 300 } e ? e[..300] : first.DeadLetterErrorDescription,
                [.. rows.Select(r => r.EntityName).Distinct().Order(StringComparer.Ordinal)], rows.Count, rows.Count(r => r.Status == DlqMessageStatus.Active),
                first.DetectedAtUtc, newest.DetectedAtUtc, daily, recent >= 3 && recent >= 2 * Math.Max(1, earlier),
                new SignatureReplays(mine.Count, fixedCount, returned, mine.Count - verified), verdict));
        }

        return result;
    }
}
