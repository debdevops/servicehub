using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure;

/// <summary>
/// Manages operational knowledge for FailureSignatures.
///
/// This service provides persistent storage and retrieval of institutional memory:
/// root cause analysis, resolution steps, ownership, and operational guidance.
/// </summary>
public sealed class FailureKnowledgeService : IFailureKnowledgeService
{
    private readonly DlqDbContext _dbContext;
    private readonly ILogger<FailureKnowledgeService> _logger;

    public FailureKnowledgeService(
        DlqDbContext dbContext,
        ILogger<FailureKnowledgeService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<FailureKnowledge>> GetKnowledgeAsync(
        string ownerId,
        Guid namespaceId,
        string signatureHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);
        ArgumentNullException.ThrowIfNull(signatureHash);

        var result = await GetKnowledgeBatchAsync(ownerId, namespaceId, [signatureHash], cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result.Failure<FailureKnowledge>(result.Error);
        }

        return Result.Success(result.Value[signatureHash]);
    }

    public async Task<Result<FailureKnowledge>> UpsertKnowledgeAsync(
        string ownerId,
        Guid namespaceId,
        string signatureHash,
        FailureKnowledge knowledge,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);
        ArgumentNullException.ThrowIfNull(signatureHash);
        ArgumentNullException.ThrowIfNull(knowledge);

        var entity = await _dbContext.FailureKnowledgeEntities
            .FirstOrDefaultAsync(
                e => e.OwnerId == ownerId && e.NamespaceId == namespaceId && e.SignatureHash == signatureHash,
                cancellationToken)
            .ConfigureAwait(false);

        if (entity == null)
        {
            // Create new
            entity = new FailureKnowledgeEntity
            {
                NamespaceId = namespaceId,
                OwnerId = ownerId,
                SignatureHash = signatureHash,
                RootCause = knowledge.RootCause,
                ResolutionNotes = knowledge.ResolutionNotes,
                OperationalNotes = knowledge.OperationalNotes,
                RunbookLink = knowledge.RunbookLink,
                Owner = knowledge.Owner,
                ReplayGuidance = knowledge.ReplayGuidance,
                LastUpdatedAt = DateTimeOffset.UtcNow,
                KnowledgeVersion = knowledge.KnowledgeVersion,
                ReviewDueAt = knowledge.ReviewDueAt,
                Tags = knowledge.Tags,
            };

            _dbContext.FailureKnowledgeEntities.Add(entity);
        }
        else
        {
            // Update existing
            entity.RootCause = knowledge.RootCause;
            entity.ResolutionNotes = knowledge.ResolutionNotes;
            entity.OperationalNotes = knowledge.OperationalNotes;
            entity.RunbookLink = knowledge.RunbookLink;
            entity.Owner = knowledge.Owner;
            entity.ReplayGuidance = knowledge.ReplayGuidance;
            entity.LastUpdatedAt = DateTimeOffset.UtcNow;
            entity.KnowledgeVersion = knowledge.KnowledgeVersion + 1;
            entity.ReviewDueAt = knowledge.ReviewDueAt;
            entity.Tags = knowledge.Tags;
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var result = new FailureKnowledge(
            RootCause: entity.RootCause,
            ResolutionNotes: entity.ResolutionNotes,
            OperationalNotes: entity.OperationalNotes,
            RunbookLink: entity.RunbookLink,
            Owner: entity.Owner,
            ReplayGuidance: entity.ReplayGuidance,
            LastUpdatedAt: entity.LastUpdatedAt,
            KnowledgeVersion: entity.KnowledgeVersion,
            ReviewDueAt: entity.ReviewDueAt,
            Tags: entity.Tags);

        return Result.Success(result);
    }

    public async Task<Result<IReadOnlyDictionary<string, FailureKnowledge>>> GetKnowledgeBatchAsync(
        string ownerId,
        Guid namespaceId,
        IReadOnlyList<string> signatureHashes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);
        ArgumentNullException.ThrowIfNull(signatureHashes);

        if (signatureHashes.Count == 0)
        {
            return Result.Success((IReadOnlyDictionary<string, FailureKnowledge>)new Dictionary<string, FailureKnowledge>());
        }

        // Fetch existing knowledge from database
        var existingKnowledge = await _dbContext.FailureKnowledgeEntities
            .Where(e => e.OwnerId == ownerId &&
                       e.NamespaceId == namespaceId &&
                       signatureHashes.Contains(e.SignatureHash))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = new Dictionary<string, FailureKnowledge>(signatureHashes.Count);

        // Map existing knowledge
        var byHash = existingKnowledge.ToDictionary(e => e.SignatureHash);
        foreach (var hash in signatureHashes)
        {
            if (byHash.TryGetValue(hash, out var entity))
            {
                result[hash] = new FailureKnowledge(
                    RootCause: entity.RootCause,
                    ResolutionNotes: entity.ResolutionNotes,
                    OperationalNotes: entity.OperationalNotes,
                    RunbookLink: entity.RunbookLink,
                    Owner: entity.Owner,
                    ReplayGuidance: entity.ReplayGuidance,
                    LastUpdatedAt: entity.LastUpdatedAt,
                    KnowledgeVersion: entity.KnowledgeVersion,
                    ReviewDueAt: entity.ReviewDueAt,
                    Tags: entity.Tags);
            }
            else
            {
                // Default empty knowledge for missing hashes
                result[hash] = new FailureKnowledge(
                    RootCause: null,
                    ResolutionNotes: null,
                    OperationalNotes: null,
                    RunbookLink: null,
                    Owner: null,
                    ReplayGuidance: null,
                    LastUpdatedAt: null,
                    KnowledgeVersion: 1,
                    ReviewDueAt: null,
                    Tags: null);
            }
        }

        return Result.Success((IReadOnlyDictionary<string, FailureKnowledge>)result);
    }

    public async Task<Result<FailureKnowledge>> MarkForReviewAsync(
        string ownerId,
        Guid namespaceId,
        string signatureHash,
        DateTimeOffset reviewDueAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);
        ArgumentNullException.ThrowIfNull(signatureHash);

        var entity = await _dbContext.FailureKnowledgeEntities
            .FirstOrDefaultAsync(
                e => e.OwnerId == ownerId && e.NamespaceId == namespaceId && e.SignatureHash == signatureHash,
                cancellationToken)
            .ConfigureAwait(false);

        if (entity == null)
        {
            // Create with empty knowledge, but set ReviewDueAt
            entity = new FailureKnowledgeEntity
            {
                NamespaceId = namespaceId,
                OwnerId = ownerId,
                SignatureHash = signatureHash,
                ReviewDueAt = reviewDueAt,
                LastUpdatedAt = DateTimeOffset.UtcNow,
            };

            _dbContext.FailureKnowledgeEntities.Add(entity);
        }
        else
        {
            entity.ReviewDueAt = reviewDueAt;
            entity.LastUpdatedAt = DateTimeOffset.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var result = new FailureKnowledge(
            RootCause: entity.RootCause,
            ResolutionNotes: entity.ResolutionNotes,
            OperationalNotes: entity.OperationalNotes,
            RunbookLink: entity.RunbookLink,
            Owner: entity.Owner,
            ReplayGuidance: entity.ReplayGuidance,
            LastUpdatedAt: entity.LastUpdatedAt,
            KnowledgeVersion: entity.KnowledgeVersion,
            ReviewDueAt: entity.ReviewDueAt,
            Tags: entity.Tags);

        return Result.Success(result);
    }
}
