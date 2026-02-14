using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure;

/// <summary>
/// Provides query and management operations for DLQ message history.
/// </summary>
public sealed class DlqHistoryService : IDlqHistoryService
{
    private readonly DlqDbContext _dbContext;
    private readonly ILogger<DlqHistoryService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DlqHistoryService"/> class.
    /// </summary>
    public DlqHistoryService(DlqDbContext dbContext, ILogger<DlqHistoryService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Result<DlqHistoryPageResult>> GetHistoryAsync(
        Guid? namespaceId = null,
        string? entityName = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        DlqMessageStatus? status = null,
        FailureCategory? category = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbContext.DlqMessages.AsNoTracking().AsQueryable();

            if (namespaceId.HasValue)
                query = query.Where(m => m.NamespaceId == namespaceId.Value);

            if (!string.IsNullOrWhiteSpace(entityName))
                query = query.Where(m => m.EntityName.Contains(entityName));

            if (from.HasValue)
                query = query.Where(m => m.DetectedAtUtc >= from.Value);

            if (to.HasValue)
                query = query.Where(m => m.DetectedAtUtc <= to.Value);

            if (status.HasValue)
                query = query.Where(m => m.Status == status.Value);

            if (category.HasValue)
                query = query.Where(m => m.FailureCategory == category.Value);

            var totalCount = await query.CountAsync(cancellationToken);

            var items = await query
                .OrderByDescending(m => m.DetectedAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            var result = new DlqHistoryPageResult(
                Items: items,
                TotalCount: totalCount,
                Page: page,
                PageSize: pageSize,
                HasNextPage: page * pageSize < totalCount,
                HasPreviousPage: page > 1);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query DLQ history");
            return Result<DlqHistoryPageResult>.Failure(
                Error.Internal("Dlq.QueryFailed", $"Failed to query DLQ history: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<Result<DlqMessage>> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = await _dbContext.DlqMessages
                .AsNoTracking()
                .Include(m => m.ReplayHistories)
                .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

            if (message == null)
                return Result<DlqMessage>.Failure(Error.NotFound("Dlq.NotFound", $"DLQ message with ID {id} was not found"));

            return message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get DLQ message {Id}", id);
            return Result<DlqMessage>.Failure(
                Error.Internal("Dlq.GetFailed", $"Failed to get DLQ message: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<DlqTimelineEvent>>> GetTimelineAsync(
        long id, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = await _dbContext.DlqMessages
                .AsNoTracking()
                .Include(m => m.ReplayHistories)
                .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

            if (message == null)
                return Result<IReadOnlyList<DlqTimelineEvent>>.Failure(
                    Error.NotFound("Dlq.NotFound", $"DLQ message with ID {id} was not found"));

            var events = new List<DlqTimelineEvent>();

            // 1. Message enqueued
            events.Add(new DlqTimelineEvent(
                EventType: "Enqueued",
                Description: "Message was enqueued to the entity",
                Timestamp: message.EnqueuedTimeUtc,
                Details: new Dictionary<string, string>
                {
                    ["Entity"] = message.EntityName,
                    ["MessageId"] = message.MessageId
                }));

            // 2. Dead-lettered
            if (message.DeadLetterTimeUtc.HasValue)
            {
                var details = new Dictionary<string, string>
                {
                    ["Reason"] = message.DeadLetterReason ?? "Unknown",
                    ["DeliveryCount"] = message.DeliveryCount.ToString()
                };
                if (!string.IsNullOrEmpty(message.DeadLetterErrorDescription))
                    details["ErrorDescription"] = message.DeadLetterErrorDescription;

                events.Add(new DlqTimelineEvent(
                    EventType: "DeadLettered",
                    Description: $"Message moved to DLQ: {message.DeadLetterReason ?? "Unknown reason"}",
                    Timestamp: message.DeadLetterTimeUtc.Value,
                    Details: details));
            }

            // 3. Detected by monitor
            events.Add(new DlqTimelineEvent(
                EventType: "Detected",
                Description: "Message detected by DLQ monitor",
                Timestamp: message.DetectedAtUtc,
                Details: new Dictionary<string, string>
                {
                    ["Category"] = message.FailureCategory.ToString(),
                    ["Confidence"] = $"{message.CategoryConfidence:P0}"
                }));

            // 4. Replay attempts
            if (message.ReplayHistories?.Count > 0)
            {
                foreach (var replay in message.ReplayHistories.OrderBy(r => r.ReplayedAt))
                {
                    events.Add(new DlqTimelineEvent(
                        EventType: replay.OutcomeStatus == "Success" ? "ReplayedSuccess" : "ReplayedFailed",
                        Description: $"Replay to {replay.ReplayedToEntity}: {replay.OutcomeStatus}",
                        Timestamp: replay.ReplayedAt,
                        Details: new Dictionary<string, string>
                        {
                            ["Strategy"] = replay.ReplayStrategy,
                            ["ReplayedBy"] = replay.ReplayedBy,
                            ["Outcome"] = replay.OutcomeStatus
                        }));
                }
            }

            // 5. Current status events
            if (message.ReplayedAt.HasValue)
            {
                events.Add(new DlqTimelineEvent(
                    EventType: "StatusChanged",
                    Description: $"Status changed to {message.Status}",
                    Timestamp: message.ReplayedAt.Value));
            }

            if (message.ArchivedAt.HasValue)
            {
                events.Add(new DlqTimelineEvent(
                    EventType: "Archived",
                    Description: "Message archived",
                    Timestamp: message.ArchivedAt.Value));
            }

            var sorted = events.OrderBy(e => e.Timestamp).ToList();
            return Result<IReadOnlyList<DlqTimelineEvent>>.Success(sorted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build timeline for DLQ message {Id}", id);
            return Result<IReadOnlyList<DlqTimelineEvent>>.Failure(
                Error.Internal("Dlq.TimelineFailed", $"Failed to build timeline: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<Result<DlqMessage>> UpdateNotesAsync(
        long id, string notes, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = await _dbContext.DlqMessages.FindAsync(new object[] { id }, cancellationToken);
            if (message == null)
                return Result<DlqMessage>.Failure(Error.NotFound("Dlq.NotFound", $"DLQ message with ID {id} was not found"));

            message.UserNotes = notes;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update notes for DLQ message {Id}", id);
            return Result<DlqMessage>.Failure(
                Error.Internal("Dlq.UpdateFailed", $"Failed to update notes: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<Result<DlqSummary>> GetSummaryAsync(
        Guid? namespaceId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbContext.DlqMessages.AsNoTracking().AsQueryable();

            if (namespaceId.HasValue)
                query = query.Where(m => m.NamespaceId == namespaceId.Value);

            var total = await query.CountAsync(cancellationToken);
            var active = await query.Where(m => m.Status == DlqMessageStatus.Active).CountAsync(cancellationToken);
            var replayed = await query.Where(m => m.Status == DlqMessageStatus.Replayed).CountAsync(cancellationToken);
            var archived = await query.Where(m => m.Status == DlqMessageStatus.Archived).CountAsync(cancellationToken);

            // Only count actionable messages in breakdown views
            var actionableQuery = query.Where(m => m.Status == DlqMessageStatus.Active);

            var byCategory = await actionableQuery
                .GroupBy(m => m.FailureCategory)
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Category.ToString(), x => x.Count, cancellationToken);

            var byEntity = await actionableQuery
                .GroupBy(m => m.EntityName)
                .Select(g => new { Entity = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(20)
                .ToDictionaryAsync(x => x.Entity, x => x.Count, cancellationToken);

            var oldest = await query
                .OrderBy(m => m.DetectedAtUtc)
                .Select(m => (DateTimeOffset?)m.DetectedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            var newest = await query
                .OrderByDescending(m => m.DetectedAtUtc)
                .Select(m => (DateTimeOffset?)m.DetectedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            // Daily trend for the last 30 days
            var thirtyDaysAgo = DateTimeOffset.UtcNow.AddDays(-30);

            // SQLite cannot translate DateTimeOffset.Date grouping reliably, so aggregate in-memory
            var detectedTimestamps = await query
                .Where(m => m.DetectedAtUtc >= thirtyDaysAgo)
                .Select(m => m.DetectedAtUtc)
                .ToListAsync(cancellationToken);

            var replayedTimestamps = await query
                .Where(m => m.ReplayedAt.HasValue && m.ReplayedAt.Value >= thirtyDaysAgo)
                .Select(m => m.ReplayedAt!.Value)
                .ToListAsync(cancellationToken);

            var dailyNew = detectedTimestamps
                .GroupBy(d => d.UtcDateTime.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .OrderBy(x => x.Date)
                .ToList();

            var dailyResolved = replayedTimestamps
                .GroupBy(d => d.UtcDateTime.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .OrderBy(x => x.Date)
                .ToList();

            var allDates = dailyNew.Select(d => d.Date)
                .Union(dailyResolved.Select(d => d.Date))
                .OrderBy(d => d)
                .ToList();

            var trend = allDates.Select(date => new DlqTrendPoint(
                Date: new DateTimeOffset(date, TimeSpan.Zero),
                NewMessages: dailyNew.FirstOrDefault(d => d.Date == date)?.Count ?? 0,
                ResolvedMessages: dailyResolved.FirstOrDefault(d => d.Date == date)?.Count ?? 0
            )).ToList();

            var summary = new DlqSummary(
                TotalMessages: total,
                ActiveMessages: active,
                ReplayedMessages: replayed,
                ArchivedMessages: archived,
                ByCategory: byCategory,
                ByEntity: byEntity,
                OldestMessage: oldest,
                NewestMessage: newest,
                DailyTrend: trend);

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate DLQ summary");
            return Result<DlqSummary>.Failure(
                Error.Internal("Dlq.SummaryFailed", $"Failed to generate summary: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<DlqMessage>>> ExportAsync(
        Guid? namespaceId = null,
        string? entityName = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        DlqMessageStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbContext.DlqMessages.AsNoTracking().AsQueryable();

            if (namespaceId.HasValue)
                query = query.Where(m => m.NamespaceId == namespaceId.Value);

            if (!string.IsNullOrWhiteSpace(entityName))
                query = query.Where(m => m.EntityName.Contains(entityName));

            if (from.HasValue)
                query = query.Where(m => m.DetectedAtUtc >= from.Value);

            if (to.HasValue)
                query = query.Where(m => m.DetectedAtUtc <= to.Value);

            if (status.HasValue)
                query = query.Where(m => m.Status == status.Value);

            var messages = await query
                .OrderByDescending(m => m.DetectedAtUtc)
                .Take(10000)
                .ToListAsync(cancellationToken);

            return Result<IReadOnlyList<DlqMessage>>.Success(messages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export DLQ messages");
            return Result<IReadOnlyList<DlqMessage>>.Failure(
                Error.Internal("Dlq.ExportFailed", $"Failed to export messages: {ex.Message}"));
        }
    }
}
