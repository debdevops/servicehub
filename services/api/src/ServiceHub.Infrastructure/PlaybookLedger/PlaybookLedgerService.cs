using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.PlaybookLedger;

/// <summary>
/// The Playbook Ledger's service implementation (M4 of the persistence wave) — structurally
/// mirrors <c>RecoveryLedgerService</c> (per-owner lock, an <c>AppendEventAsync</c> helper that
/// considers both persisted and same-unit-of-work tracked events, <c>LogRedactor.Redact</c> on
/// every JSON write before hashing/persisting) against a fully independent chain and schema.
/// </summary>
public sealed class PlaybookLedgerService : IPlaybookLedger
{
    private readonly DlqDbContext _dbContext;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> OwnerLocks = new();
    private const int SchemaVersion = 1;

    public PlaybookLedgerService(DlqDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    private static async Task<IDisposable> AcquireOwnerLockAsync(string ownerId, CancellationToken cancellationToken)
    {
        var semaphore = OwnerLocks.GetOrAdd(ownerId, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }

    private static bool IsTerminal(PlaybookEntryState state) =>
        state is PlaybookEntryState.Approved or PlaybookEntryState.Rejected
            or PlaybookEntryState.Expired or PlaybookEntryState.Superseded;

    private async Task<(long Seq, string PrevHash)> GetNextSeqAndPrevHashAsync(string ownerId, CancellationToken cancellationToken)
    {
        // Considers not-yet-SaveChanges'd tracked Added events in this unit of work, not just
        // persisted rows — matters when a single logical operation appends more than one event
        // before its final SaveChangesAsync.
        var trackedForOwner = _dbContext.ChangeTracker.Entries<PlaybookEvent>()
            .Where(e => e.State == EntityState.Added && e.Entity.OwnerId == ownerId)
            .Select(e => e.Entity)
            .ToList();

        var persistedLast = await _dbContext.PlaybookEvents
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId)
            .OrderByDescending(e => e.Seq)
            .FirstOrDefaultAsync(cancellationToken);

        var latestTracked = trackedForOwner.OrderByDescending(e => e.Seq).FirstOrDefault();

        if (latestTracked is not null && latestTracked.Seq > (persistedLast?.Seq ?? 0))
        {
            return (latestTracked.Seq + 1, latestTracked.EntryHash);
        }

        return persistedLast is null
            ? (1L, PlaybookHashChain.GenesisHash)
            : (persistedLast.Seq + 1, persistedLast.EntryHash);
    }

    private async Task<PlaybookEvent> AppendEventAsync(
        PlaybookEntry entry, PlaybookEventType eventType, PlaybookActor actor, string? detail, CancellationToken cancellationToken)
    {
        var (seq, prevHash) = await GetNextSeqAndPrevHashAsync(entry.OwnerId, cancellationToken);
        var redactedDetail = detail is null ? null : LogRedactor.Redact(detail);
        var id = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;

        var entryHash = PlaybookHashChain.ComputeEntryHash(
            id, entry.OwnerId, seq, entry.Id, eventType, occurredAt,
            actor.Identity, actor.Kind, redactedDetail, SchemaVersion, prevHash);

        var evt = new PlaybookEvent
        {
            Id = id,
            OwnerId = entry.OwnerId,
            Seq = seq,
            EntryId = entry.Id,
            EventType = eventType,
            OccurredAt = occurredAt,
            ActorIdentity = actor.Identity,
            ActorKind = actor.Kind,
            DetailJson = redactedDetail,
            PrevHash = prevHash,
            EntryHash = entryHash,
            SchemaVersion = SchemaVersion,
        };

        _dbContext.PlaybookEvents.Add(evt);
        return evt;
    }

    private async Task<Result<PlaybookEntry>> LoadEntryAsync(Guid entryId, string ownerId, CancellationToken cancellationToken)
    {
        var entry = await _dbContext.PlaybookEntries.FirstOrDefaultAsync(e => e.Id == entryId && e.OwnerId == ownerId, cancellationToken);
        return entry is null
            ? Result.Failure<PlaybookEntry>(Error.NotFound(ErrorCodes.Playbook.NotFound, $"Entry with ID '{entryId}' was not found."))
            : Result.Success(entry);
    }

    /// <inheritdoc/>
    public async Task<Result<PlaybookEntry>> ProposeAsync(ProposePlaybookEntryRequest request, CancellationToken cancellationToken = default)
    {
        using var _ = await AcquireOwnerLockAsync(request.OwnerId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var entry = new PlaybookEntry
        {
            OwnerId = request.OwnerId,
            PillarKind = request.PillarKind,
            ProposalKind = request.ProposalKind,
            EvidenceRefJson = LogRedactor.Redact(request.EvidenceRefJson),
            ProposalJson = LogRedactor.Redact(request.ProposalJson),
            ProposedAt = now,
            ProposerIdentity = request.Proposer.Identity,
            ProposerKind = request.Proposer.Kind,
            SignatureHashSnapshot = request.SignatureHashSnapshot,
            NamespaceId = request.NamespaceId,
            NamespaceNameSnapshot = request.NamespaceNameSnapshot,
            ProviderSnapshot = request.ProviderSnapshot,
            EnvironmentSnapshot = request.EnvironmentSnapshot,
            RelatedRecoveryOperationId = request.RelatedRecoveryOperationId,
            ExpiresAt = now.Add(request.ExpiresAfter),
            State = PlaybookEntryState.Proposed,
        };

        _dbContext.PlaybookEntries.Add(entry);

        var evt = await AppendEventAsync(entry, PlaybookEventType.Proposed, request.Proposer, detail: null, cancellationToken);
        entry.LastEventSeq = evt.Seq;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(entry);
    }

    /// <inheritdoc/>
    public async Task<Result<PlaybookEntry>> MarkUnderReviewAsync(Guid entryId, string ownerId, PlaybookActor actor, CancellationToken cancellationToken = default)
    {
        using var _ = await AcquireOwnerLockAsync(ownerId, cancellationToken);

        var entryResult = await LoadEntryAsync(entryId, ownerId, cancellationToken);
        if (entryResult.IsFailure) return entryResult;
        var entry = entryResult.Value;

        if (entry.State != PlaybookEntryState.Proposed)
        {
            return Result.Failure<PlaybookEntry>(Error.Conflict(
                ErrorCodes.Playbook.InvalidTransition, $"Cannot mark UnderReview from state {entry.State}."));
        }

        entry.State = PlaybookEntryState.UnderReview;
        var evt = await AppendEventAsync(entry, PlaybookEventType.UnderReview, actor, detail: null, cancellationToken);
        entry.LastEventSeq = evt.Seq;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(entry);
    }

    /// <inheritdoc/>
    public async Task<Result<PlaybookEntry>> ReviseAsync(Guid entryId, string ownerId, PlaybookActor actor, string revisedProposalJson, CancellationToken cancellationToken = default)
    {
        using var _ = await AcquireOwnerLockAsync(ownerId, cancellationToken);

        var entryResult = await LoadEntryAsync(entryId, ownerId, cancellationToken);
        if (entryResult.IsFailure) return entryResult;
        var entry = entryResult.Value;

        if (entry.State is not (PlaybookEntryState.Proposed or PlaybookEntryState.UnderReview))
        {
            return Result.Failure<PlaybookEntry>(Error.Conflict(
                ErrorCodes.Playbook.InvalidTransition, $"Cannot revise from state {entry.State}."));
        }

        entry.State = PlaybookEntryState.Edited;
        var evt = await AppendEventAsync(entry, PlaybookEventType.Revised, actor, revisedProposalJson, cancellationToken);
        entry.LastEventSeq = evt.Seq;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(entry);
    }

    /// <inheritdoc/>
    public async Task<Result<PlaybookEntry>> DispositionAsync(
        Guid entryId, string ownerId, PlaybookActor actor, PlaybookDisposition disposition, string? reason, CancellationToken cancellationToken = default)
    {
        if (disposition == PlaybookDisposition.Rejected && string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure<PlaybookEntry>(Error.Validation(
                ErrorCodes.Playbook.ReasonRequired, "A reason is required to reject a proposal."));
        }

        using var _ = await AcquireOwnerLockAsync(ownerId, cancellationToken);

        var entryResult = await LoadEntryAsync(entryId, ownerId, cancellationToken);
        if (entryResult.IsFailure) return entryResult;
        var entry = entryResult.Value;

        if (entry.State is not (PlaybookEntryState.Proposed or PlaybookEntryState.UnderReview or PlaybookEntryState.Edited))
        {
            return Result.Failure<PlaybookEntry>(Error.Conflict(
                ErrorCodes.Playbook.InvalidTransition, $"Cannot disposition from state {entry.State}."));
        }

        entry.State = disposition == PlaybookDisposition.Approved ? PlaybookEntryState.Approved : PlaybookEntryState.Rejected;
        entry.Disposition = disposition;
        entry.ClosedAt = DateTimeOffset.UtcNow;

        var eventType = disposition == PlaybookDisposition.Approved ? PlaybookEventType.Approved : PlaybookEventType.Rejected;
        var evt = await AppendEventAsync(entry, eventType, actor, reason, cancellationToken);
        entry.LastEventSeq = evt.Seq;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(entry);
    }

    /// <inheritdoc/>
    public async Task<Result<PlaybookEntry>> ExpireAsync(Guid entryId, string ownerId, CancellationToken cancellationToken = default)
    {
        using var _ = await AcquireOwnerLockAsync(ownerId, cancellationToken);

        var entryResult = await LoadEntryAsync(entryId, ownerId, cancellationToken);
        if (entryResult.IsFailure) return entryResult;
        var entry = entryResult.Value;

        if (IsTerminal(entry.State))
        {
            // Idempotent no-op — a background sweep may see the same entry more than once.
            return Result.Success(entry);
        }

        var systemActor = new PlaybookActor("System:PlaybookExpiry", PlaybookActorKind.System);
        entry.State = PlaybookEntryState.Expired;
        entry.ClosedAt = DateTimeOffset.UtcNow;

        var evt = await AppendEventAsync(entry, PlaybookEventType.Expired, systemActor, detail: null, cancellationToken);
        entry.LastEventSeq = evt.Seq;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(entry);
    }

    /// <inheritdoc/>
    public async Task<Result<PlaybookEntry>> SupersedeAsync(Guid entryId, string ownerId, PlaybookActor actor, Guid supersededByEntryId, CancellationToken cancellationToken = default)
    {
        using var _ = await AcquireOwnerLockAsync(ownerId, cancellationToken);

        var entryResult = await LoadEntryAsync(entryId, ownerId, cancellationToken);
        if (entryResult.IsFailure) return entryResult;
        var entry = entryResult.Value;

        if (entry.State is not (PlaybookEntryState.Proposed or PlaybookEntryState.UnderReview))
        {
            return Result.Failure<PlaybookEntry>(Error.Conflict(
                ErrorCodes.Playbook.InvalidTransition, $"Cannot supersede from state {entry.State}."));
        }

        entry.State = PlaybookEntryState.Superseded;
        entry.ClosedAt = DateTimeOffset.UtcNow;

        var detail = System.Text.Json.JsonSerializer.Serialize(new { supersededByEntryId });
        var evt = await AppendEventAsync(entry, PlaybookEventType.Superseded, actor, detail, cancellationToken);
        entry.LastEventSeq = evt.Seq;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(entry);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<PlaybookEntry>>> QueryEntriesAsync(
        string ownerId, PillarKind? pillarKind = null, Guid? namespaceId = null, PlaybookEntryState? state = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.PlaybookEntries.AsNoTracking().Where(e => e.OwnerId == ownerId);

        if (pillarKind is not null)
        {
            query = query.Where(e => e.PillarKind == pillarKind);
        }

        if (namespaceId is not null)
        {
            query = query.Where(e => e.NamespaceId == namespaceId);
        }

        if (state is not null)
        {
            query = query.Where(e => e.State == state);
        }

        var entries = await query.OrderByDescending(e => e.ProposedAt).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<PlaybookEntry>>(entries);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<PlaybookEvent>>> GetEventsForEntryAsync(Guid entryId, string ownerId, CancellationToken cancellationToken = default)
    {
        var entryExists = await _dbContext.PlaybookEntries.AsNoTracking()
            .AnyAsync(e => e.Id == entryId && e.OwnerId == ownerId, cancellationToken);
        if (!entryExists)
        {
            return Result.Failure<IReadOnlyList<PlaybookEvent>>(Error.NotFound(
                ErrorCodes.Playbook.NotFound, $"Entry with ID '{entryId}' was not found."));
        }

        var events = await _dbContext.PlaybookEvents.AsNoTracking()
            .Where(e => e.EntryId == entryId && e.OwnerId == ownerId)
            .OrderBy(e => e.Seq)
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<PlaybookEvent>>(events);
    }

    /// <inheritdoc/>
    public async Task<PlaybookEntry?> GetEntryAsync(Guid entryId, string ownerId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.PlaybookEntries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entryId && e.OwnerId == ownerId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ChainVerificationResult> VerifyChainAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var events = await _dbContext.PlaybookEvents.AsNoTracking()
            .Where(e => e.OwnerId == ownerId)
            .OrderBy(e => e.Seq)
            .ToListAsync(cancellationToken);

        return PlaybookChainVerifier.Verify(ownerId, events);
    }
}
