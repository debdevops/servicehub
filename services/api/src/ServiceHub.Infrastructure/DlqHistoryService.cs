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
        string ownerId,
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

            // TENANT ISOLATION: Filter messages by owner
            query = query.Where(m => m.OwnerId == ownerId);

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
    public async Task<Result<DlqMessage>> GetByIdAsync(string ownerId, long id, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = await _dbContext.DlqMessages
                .AsNoTracking()
                .Include(m => m.ReplayHistories)
                .FirstOrDefaultAsync(m => m.Id == id && m.OwnerId == ownerId, cancellationToken);

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
    public async Task<Result<IReadOnlyList<DlqMessage>>> GetByIdsAsync(
        string ownerId, IReadOnlyList<long> ids, CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = await _dbContext.DlqMessages
                .AsNoTracking()
                .Where(m => ids.Contains(m.Id) && m.OwnerId == ownerId)
                .OrderByDescending(m => m.DetectedAtUtc)
                .ToListAsync(cancellationToken);

            return Result<IReadOnlyList<DlqMessage>>.Success(messages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get DLQ messages by ID");
            return Result<IReadOnlyList<DlqMessage>>.Failure(
                Error.Internal("Dlq.GetFailed", $"Failed to get DLQ messages: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<DlqTimelineEvent>>> GetTimelineAsync(
        string ownerId, long id, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = await _dbContext.DlqMessages
                .AsNoTracking()
                .Include(m => m.ReplayHistories)
                .FirstOrDefaultAsync(m => m.Id == id && m.OwnerId == ownerId, cancellationToken);

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
        string ownerId, long id, string notes, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = await _dbContext.DlqMessages
                .FirstOrDefaultAsync(m => m.Id == id && m.OwnerId == ownerId, cancellationToken);
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

    // Manual triage may only move a message to one of these lifecycle states. Replayed /
    // ReplayFailed are outcomes of the replay flow and must not be settable by hand, so a
    // "Replayed" status always corresponds to a real replay attempt. Discarded must mean
    // "ServiceHub destroyed this message via a provider call" — an operator has no such call to
    // point to, so manual triage cannot declare it; Resolved (with ResolutionCause.DeclaredByOperator)
    // is the honest equivalent for a human-observed removal.
    private static readonly HashSet<DlqMessageStatus> ManualTriageTargets =
    [
        DlqMessageStatus.Active,
        DlqMessageStatus.Archived,
        DlqMessageStatus.Resolved
    ];

    /// <inheritdoc />
    public async Task<Result<DlqMessage>> UpdateStatusAsync(
        string ownerId,
        long id,
        DlqMessageStatus newStatus,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        if (!ManualTriageTargets.Contains(newStatus))
        {
            return Result<DlqMessage>.Failure(Error.Validation(
                "Dlq.InvalidStatusTransition",
                $"'{newStatus}' is not a valid triage status. Allowed: Active, Archived, Resolved."));
        }

        try
        {
            var message = await _dbContext.DlqMessages
                .FirstOrDefaultAsync(m => m.Id == id && m.OwnerId == ownerId, cancellationToken);
            if (message == null)
                return Result<DlqMessage>.Failure(Error.NotFound("Dlq.NotFound", $"DLQ message with ID {id} was not found"));

            var now = DateTimeOffset.UtcNow;
            message.Status = newStatus;

            // Each transition stamps its own timestamp and clears the other, so a message
            // moving e.g. Archived -> Resolved doesn't retain a stale ArchivedAt.
            switch (newStatus)
            {
                case DlqMessageStatus.Archived:
                    message.ArchivedAt = now;
                    message.ResolvedAt = null;
                    message.ResolutionCause = null;
                    break;
                case DlqMessageStatus.Resolved:
                    message.ResolvedAt = now;
                    message.ArchivedAt = null;
                    // The operator is declaring this resolved on their own observation, not a
                    // ServiceHub provider call — that is the honest cause, not a guess.
                    message.ResolutionCause = DlqResolutionCause.DeclaredByOperator;
                    break;
                case DlqMessageStatus.Active:
                    // Re-opening a triaged message: clear the resolution stamps.
                    message.ArchivedAt = null;
                    message.ResolvedAt = null;
                    message.ResolutionCause = null;
                    break;
            }

            if (!string.IsNullOrWhiteSpace(notes))
            {
                message.UserNotes = string.IsNullOrWhiteSpace(message.UserNotes)
                    ? notes
                    : $"{message.UserNotes}{Environment.NewLine}{notes}";
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "DLQ message {Id} triaged to {Status} for owner {OwnerId}", id, newStatus, ownerId);

            return message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update status for DLQ message {Id}", id);
            return Result<DlqMessage>.Failure(
                Error.Internal("Dlq.UpdateFailed", $"Failed to update status: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<Result<DlqSummary>> GetSummaryAsync(
        string ownerId, Guid? namespaceId = null, int days = 30, CancellationToken cancellationToken = default)
    {
        try
        {
            // Clamp days to a sensible range
            days = Math.Clamp(days, 1, 365);
            var query = _dbContext.DlqMessages.AsNoTracking().AsQueryable();

            // TENANT ISOLATION: Filter messages by owner
            query = query.Where(m => m.OwnerId == ownerId);

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

            // Daily trend for the configured number of days
            var cutoffDate = DateTimeOffset.UtcNow.AddDays(-days);

            // SQLite cannot translate DateTimeOffset.Date grouping reliably, so aggregate in-memory
            var detectedTimestamps = await query
                .Where(m => m.DetectedAtUtc >= cutoffDate)
                .Select(m => m.DetectedAtUtc)
                .ToListAsync(cancellationToken);

            var replayedTimestamps = await query
                .Where(m => m.ReplayedAt.HasValue && m.ReplayedAt.Value >= cutoffDate)
                .Select(m => m.ReplayedAt!.Value)
                .ToListAsync(cancellationToken);

            var dailyNewByDate = detectedTimestamps
                .GroupBy(d => d.UtcDateTime.Date)
                .ToDictionary(g => g.Key, g => g.Count());

            var dailyResolvedByDate = replayedTimestamps
                .GroupBy(d => d.UtcDateTime.Date)
                .ToDictionary(g => g.Key, g => g.Count());

            // Zero-fill every day in the window — mirrors FleetOverviewService.BuildTrend —
            // so the trend is a continuous daily series instead of only the days that had activity.
            var trendStartDate = DateTimeOffset.UtcNow.UtcDateTime.Date.AddDays(-(days - 1));
            var trend = new List<DlqTrendPoint>(days);
            for (var i = 0; i < days; i++)
            {
                var day = trendStartDate.AddDays(i);
                trend.Add(new DlqTrendPoint(
                    Date: new DateTimeOffset(day, TimeSpan.Zero),
                    NewMessages: dailyNewByDate.GetValueOrDefault(day, 0),
                    ResolvedMessages: dailyResolvedByDate.GetValueOrDefault(day, 0)));
            }

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
        string ownerId,
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

            // TENANT ISOLATION: Filter messages by owner
            query = query.Where(m => m.OwnerId == ownerId);

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

    /// <inheritdoc />
    public async Task<Result<DlqMessage>> UpdateForensicResultAsync(
        long id,
        FailureCategory category,
        double confidence,
        string rootCause,
        string replaySafety,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var message = await _dbContext.DlqMessages.FindAsync(new object[] { id }, cancellationToken);
            if (message == null)
                return Result<DlqMessage>.Failure(
                    Error.NotFound("Dlq.NotFound", $"DLQ message with ID {id} was not found"));

            message.FailureCategory = category;
            message.CategoryConfidence = confidence;
            message.ForensicRootCause = rootCause;
            message.ForensicConfidence = confidence;
            message.ReplaySafety = replaySafety;

            await _dbContext.SaveChangesAsync(cancellationToken);
            return message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update forensic result for DLQ message {Id}", id);
            return Result<DlqMessage>.Failure(
                Error.Internal("Dlq.ForensicUpdateFailed", $"Failed to update forensic result: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<Result<DlqMessage>> LookupAsync(
        Guid namespaceId,
        string entityName,
        long sequenceNumber,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var message = await _dbContext.DlqMessages
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    m => m.NamespaceId == namespaceId
                         && m.EntityName == entityName
                         && m.SequenceNumber == sequenceNumber,
                    cancellationToken);

            if (message == null)
                return Result<DlqMessage>.Failure(
                    Error.NotFound("Dlq.NotFound", "No DLQ history record found for this message"));

            return message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to lookup DLQ message");
            return Result<DlqMessage>.Failure(
                Error.Internal("Dlq.LookupFailed", $"Failed to lookup DLQ message: {ex.Message}"));
        }
    }
}
