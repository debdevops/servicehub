using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure;

/// <summary>See <see cref="IExternalSignalRepository"/>. EF Core-backed against
/// <see cref="DlqDbContext.ExternalSignalEvents"/> (M5, ADR-0008).</summary>
public sealed class ExternalSignalRepository : IExternalSignalRepository
{
    private readonly DlqDbContext _dbContext;

    public ExternalSignalRepository(DlqDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task<Result<ExternalSignalEvent>> RecordAsync(
        RecordExternalSignalRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Source))
        {
            return Result<ExternalSignalEvent>.Failure(Error.Validation(
                "ExternalSignal.SourceRequired", "A source is required to record an external signal."));
        }

        var signal = new ExternalSignalEvent
        {
            OwnerId = request.OwnerId,
            NamespaceId = request.NamespaceId,
            SignalType = request.SignalType,
            OccurredAt = request.OccurredAt,
            Source = request.Source,
            DetailJson = request.DetailJson is null ? null : LogRedactor.Redact(request.DetailJson),
            IngestedAt = DateTimeOffset.UtcNow,
        };

        _dbContext.ExternalSignalEvents.Add(signal);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<ExternalSignalEvent>.Success(signal);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalSignalEvent>> QueryAsync(
        string ownerId,
        Guid? namespaceId,
        DateTimeOffset start,
        DateTimeOffset end,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.ExternalSignalEvents
            .AsNoTracking()
            .Where(s => s.OwnerId == ownerId && s.OccurredAt >= start && s.OccurredAt <= end);

        if (namespaceId is { } id)
        {
            query = query.Where(s => s.NamespaceId == id);
        }

        return await query
            .OrderByDescending(s => s.OccurredAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
