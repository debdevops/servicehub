using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Dlq;

/// <summary>Reads <c>DlqMessages</c>. Read-only: nothing here can change a row.</summary>
public sealed class DlqMessageReader : IDlqMessageReader
{
    /// <summary>Chips shown before the rest are folded into "other".</summary>
    internal const int MaxGroups = 8;

    private const int MaxEntities = 200;
    private const int MaxDescription = 300;

    private readonly ServiceHubDbContext _db;

    /// <summary>Creates the reader.</summary>
    public DlqMessageReader(ServiceHubDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

    /// <inheritdoc />
    public async Task<DlqPage> ListAsync(DlqListQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 100);

        // Everything except the reason. The chips are counted over this, so they add up to what the
        // tab holds and picking one never makes the others disappear.
        var scope = ApplyFilters(_db.DlqMessages.AsNoTracking(), query, includeReason: false);
        var filtered = ApplyFilters(_db.DlqMessages.AsNoTracking(), query, includeReason: true);

        var total = await filtered.CountAsync(ct).ConfigureAwait(false);

        var rows = await filtered
            .OrderByDescending(m => m.DetectedAtUtc)
            .ThenByDescending(m => m.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct).ConfigureAwait(false);

        var grouped = await scope
            .GroupBy(m => m.DeadLetterReason)
            .Select(g => new { Reason = g.Key, Count = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);

        var ordered = grouped
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Reason, StringComparer.Ordinal)
            .ToList();
        var groups = ordered.Take(MaxGroups).Select(g => new DlqReasonGroup(g.Reason, g.Count)).ToList();
        var rest = ordered.Skip(MaxGroups).ToList();

        var entities = await ApplyFilters(_db.DlqMessages.AsNoTracking(), query with { Entity = null }, includeReason: false)
            .Select(m => m.EntityName)
            .Distinct()
            .OrderBy(n => n)
            .Take(MaxEntities)
            .ToListAsync(ct).ConfigureAwait(false);

        return new DlqPage(
            [.. rows.Select(ToItem)],
            total,
            page,
            size,
            groups,
            rest.Count == 0 ? null : new DlqReasonGroupOther(rest.Sum(g => g.Count), rest.Count),
            entities);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DlqTrendDay>> GetTrendAsync(
        IReadOnlyCollection<Guid> namespaceIds, int days, DateTimeOffset nowUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(namespaceIds);
        days = Math.Clamp(days, 1, 30);

        var today = nowUtc.UtcDateTime.Date;
        var first = new DateTimeOffset(today.AddDays(-(days - 1)), TimeSpan.Zero);

        var seen = await _db.DlqMessages.AsNoTracking()
            .Where(m => namespaceIds.Contains(m.NamespaceId) && m.DetectedAtUtc >= first)
            .Select(m => m.DetectedAtUtc)
            .ToListAsync(ct).ConfigureAwait(false);

        var gone = await _db.DlqMessages.AsNoTracking()
            .Where(m => namespaceIds.Contains(m.NamespaceId) && m.ResolvedAt != null && m.ResolvedAt >= first)
            .Select(m => m.ResolvedAt!.Value)
            .ToListAsync(ct).ConfigureAwait(false);

        var newByDay = seen.GroupBy(t => t.UtcDateTime.Date).ToDictionary(g => g.Key, g => g.Count());
        var goneByDay = gone.GroupBy(t => t.UtcDateTime.Date).ToDictionary(g => g.Key, g => g.Count());

        return [.. Enumerable.Range(0, days).Select(i =>
        {
            var day = today.AddDays(-(days - 1) + i);
            return new DlqTrendDay(day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                newByDay.GetValueOrDefault(day), goneByDay.GetValueOrDefault(day));
        })];
    }

    /// <inheritdoc />
    public async Task<DlqDetail?> GetAsync(long id, IReadOnlyCollection<Guid> namespaceIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(namespaceIds);

        var m = await _db.DlqMessages.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && namespaceIds.Contains(x.NamespaceId), ct).ConfigureAwait(false);
        if (m is null)
        {
            return null;
        }

        var reason = m.DeadLetterReason;
        var others = await _db.DlqMessages.AsNoTracking().CountAsync(
            x => x.NamespaceId == m.NamespaceId && x.EntityName == m.EntityName && x.Status == DlqMessageStatus.Active
                 && x.Id != m.Id && x.DeadLetterReason == reason, ct).ConfigureAwait(false);

        return new DlqDetail(
            ToItem(m), m.BodyPreview, m.MessageSize > (m.BodyPreview?.Length ?? 0) && m.BodyPreview is not null,
            m.ContentType, m.CorrelationId, m.SessionId, m.ApplicationPropertiesJson, m.ResolvedAt, others, m.BodyHash);
    }

    private static IQueryable<DlqMessage> ApplyFilters(IQueryable<DlqMessage> source, DlqListQuery q, bool includeReason)
    {
        source = source.Where(m => q.NamespaceIds.Contains(m.NamespaceId));

        if (q.Status is { } status)
        {
            source = source.Where(m => m.Status == status);
        }

        if (q.Since is { } since)
        {
            source = source.Where(m => m.DetectedAtUtc >= since);
        }

        if (!string.IsNullOrWhiteSpace(q.Entity))
        {
            var entity = q.Entity.Trim();
            source = source.Where(m => m.EntityName == entity);
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            // Escaped, so a person typing % or _ finds those characters rather than everything.
            var like = $"%{q.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            source = source.Where(m =>
                EF.Functions.Like(m.MessageId, like, "\\")
                || EF.Functions.Like(m.EntityName, like, "\\")
                || (m.DeadLetterReason != null && EF.Functions.Like(m.DeadLetterReason, like, "\\")));
        }

        if (includeReason)
        {
            if (q.NoReason)
            {
                source = source.Where(m => m.DeadLetterReason == null);
            }
            else if (!string.IsNullOrEmpty(q.Reason))
            {
                var reason = q.Reason;
                source = source.Where(m => m.DeadLetterReason == reason);
            }
        }

        return source;
    }

    private static DlqListItem ToItem(DlqMessage m) => new(
        m.Id, m.NamespaceId, m.MessageId, m.SequenceNumber, m.EntityName, m.EntityType, m.TopicName,
        m.DetectedAtUtc, m.EnqueuedTimeUtc, m.DeliveryCount, m.MessageSize, m.DeadLetterReason,
        Trim(m.DeadLetterErrorDescription), m.Status, m.ResolvedAt, m.ResolutionCause);

    private static string? Trim(string? text) =>
        text is null || text.Length <= MaxDescription ? text : text[..MaxDescription] + "…";
}
