using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.RecoveryLedger;

/// <summary>
/// EF Core-backed implementation of <see cref="IRecoveryLedger"/> — the sole writer of the
/// Recovery Evidence Ledger. Owns hash-chain sequencing (per-owner, serialised by an in-process
/// semaphore and backstopped by the unique <c>(OwnerId, Seq)</c> index) and the entry state
/// machine. Append-only/immutability enforcement itself lives one layer down, in
/// <see cref="DlqDbContext.SaveChangesAsync(bool, CancellationToken)"/> via
/// <see cref="RecoveryLedgerAppendOnlyGuard"/> — this service never needs to defend against its
/// own mistakes twice, but a hand-written or future caller still can't bypass the guard.
/// </summary>
public sealed class RecoveryLedgerService : IRecoveryLedger
{
    private const int SchemaVersion = 1;
    private static readonly TimeSpan DefaultObservationWindow = TimeSpan.FromHours(24);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> OwnerLocks = new();

    // Concretely typed as HashSet<T>, not IReadOnlySet<T>: GetAgeingAsync's query below uses
    // NonTerminalStates.Contains(e.State) inside a LINQ-to-SQL expression, and EF Core's query
    // translator only recognises the Contains pattern on concrete collection types — the
    // interface-typed overload fails to translate against the SQLite provider.
    private static readonly HashSet<RecoveryEntryState> NonTerminalStates = new()
    {
        RecoveryEntryState.Executing,
        RecoveryEntryState.Observing,
        RecoveryEntryState.ExecutionUnknown,
    };

    private readonly DlqDbContext _dbContext;

    /// <summary>Initialises a new instance of <see cref="RecoveryLedgerService"/>.</summary>
    public RecoveryLedgerService(DlqDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task<Result<RecoveryOperation>> OpenOperationAsync(
        OpenRecoveryOperationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Kind == RecoveryOperationKind.Purge && string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result<RecoveryOperation>.Failure(Error.Validation(
                "RecoveryLedger.ReasonRequired", "A reason is required to open a purge operation."));
        }

        if (request.TargetCount < 0)
        {
            return Result<RecoveryOperation>.Failure(Error.Validation(
                "RecoveryLedger.InvalidTargetCount", "TargetCount cannot be negative."));
        }

        using var _ = await AcquireOwnerLockAsync(request.OwnerId, cancellationToken);

        var operation = new RecoveryOperation
        {
            OwnerId = request.OwnerId,
            Kind = request.Kind,
            Trigger = request.Trigger,
            ActorIdentity = request.Actor.Identity,
            ActorKind = request.Actor.Kind,
            ActorScopes = request.Actor.Scopes,
            Reason = request.Reason,
            IntentHeader = request.IntentHeader,
            NamespaceId = request.NamespaceId,
            NamespaceNameSnapshot = request.NamespaceNameSnapshot,
            ProviderSnapshot = request.ProviderSnapshot,
            EnvironmentSnapshot = request.EnvironmentSnapshot,
            ScopeDescription = request.ScopeDescription,
            SourceRuleId = request.SourceRuleId,
            SourceJobId = request.SourceJobId,
            CorrelationId = request.CorrelationId,
            ServiceVersion = GetServiceVersion(),
            OpenedAt = DateTimeOffset.UtcNow,
            TargetCount = request.TargetCount,
        };

        _dbContext.RecoveryOperations.Add(operation);

        await AppendEventAsync(
            operation.OwnerId, entryId: null, operation.Id,
            RecoveryEventType.OperationOpened, request.Actor, detail: null, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<RecoveryOperation>.Success(operation);
    }

    /// <inheritdoc />
    public async Task<Result<RecoveryLedgerEntry>> BeginEntryAsync(
        BeginRecoveryEntryRequest request, CancellationToken cancellationToken = default)
    {
        using var _ = await AcquireOwnerLockAsync(request.OwnerId, cancellationToken);

        var operation = await _dbContext.RecoveryOperations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == request.OperationId, cancellationToken);

        if (operation is null || operation.OwnerId != request.OwnerId)
        {
            return Result<RecoveryLedgerEntry>.Failure(Error.NotFound(
                "RecoveryLedger.OperationNotFound", "Recovery operation not found."));
        }

        var entry = new RecoveryLedgerEntry
        {
            OperationId = request.OperationId,
            OwnerId = request.OwnerId,
            DlqMessageId = request.DlqMessageId,
            NamespaceId = request.NamespaceId,
            NamespaceNameSnapshot = request.NamespaceNameSnapshot,
            ProviderSnapshot = request.ProviderSnapshot,
            EnvironmentSnapshot = request.EnvironmentSnapshot,
            EntityNameSnapshot = request.EntityNameSnapshot,
            EntityTypeSnapshot = request.EntityTypeSnapshot,
            TopicNameSnapshot = request.TopicNameSnapshot,
            SourceMessageIdSnapshot = request.SourceMessageIdSnapshot,
            SourceSequenceNumberSnapshot = request.SourceSequenceNumberSnapshot,
            BodyHash = request.BodyHash,
            FailureCategorySnapshot = request.FailureCategorySnapshot,
            DeadLetterReasonSnapshot = request.DeadLetterReasonSnapshot,
            SignatureHashSnapshot = request.SignatureHashSnapshot,
            TargetEntity = request.TargetEntity,
            BegunAt = DateTimeOffset.UtcNow,
            State = RecoveryEntryState.Executing,
        };

        _dbContext.RecoveryLedgerEntries.Add(entry);

        var evt = await AppendEventAsync(
            entry.OwnerId, entry.Id, entry.OperationId,
            RecoveryEventType.EntryBegun, request.Actor, detail: null, cancellationToken);
        entry.LastEventSeq = evt.Seq;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<RecoveryLedgerEntry>.Success(entry);
    }

    /// <inheritdoc />
    public async Task<Result<RecoveryLedgerEntry>> RecordExecutionAsync(
        RecordExecutionRequest request, CancellationToken cancellationToken = default)
    {
        using var _ = await AcquireOwnerLockAsync(request.OwnerId, cancellationToken);

        var entry = await _dbContext.RecoveryLedgerEntries
            .FirstOrDefaultAsync(e => e.Id == request.EntryId, cancellationToken);

        if (entry is null || entry.OwnerId != request.OwnerId)
        {
            return Result<RecoveryLedgerEntry>.Failure(Error.NotFound(
                "RecoveryLedger.EntryNotFound", "Recovery ledger entry not found."));
        }

        if (entry.State != RecoveryEntryState.Executing)
        {
            return Result<RecoveryLedgerEntry>.Failure(Error.Conflict(
                "RecoveryLedger.InvalidTransition",
                $"Cannot record an execution outcome for an entry in state '{entry.State}'; expected '{RecoveryEntryState.Executing}'."));
        }

        var operation = await _dbContext.RecoveryOperations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == entry.OperationId, cancellationToken);

        if (operation is null)
        {
            return Result<RecoveryLedgerEntry>.Failure(Error.NotFound(
                "RecoveryLedger.OperationNotFound", "Recovery operation not found for this entry."));
        }

        var now = DateTimeOffset.UtcNow;
        RecoveryEventType eventType;
        var opensObservationWindow = false;

        switch (request.Outcome)
        {
            case RecoveryExecutionOutcome.Accepted when operation.Kind == RecoveryOperationKind.Replay:
                entry.State = RecoveryEntryState.Observing;
                entry.ObservationWindowEndsAt = now.Add(DefaultObservationWindow);
                eventType = RecoveryEventType.ProviderAccepted;
                opensObservationWindow = true;
                break;

            case RecoveryExecutionOutcome.Accepted: // Purge
                entry.State = RecoveryEntryState.Discarded;
                entry.Disposition = RecoveryDisposition.Discarded;
                entry.ClosedAt = now;
                eventType = RecoveryEventType.ProviderAccepted;
                break;

            case RecoveryExecutionOutcome.Rejected:
                entry.State = RecoveryEntryState.ExecutionFailed;
                entry.Disposition = RecoveryDisposition.Failed;
                entry.ClosedAt = now;
                eventType = RecoveryEventType.ProviderRejected;
                break;

            case RecoveryExecutionOutcome.Unknown:
                entry.State = RecoveryEntryState.ExecutionUnknown;
                eventType = RecoveryEventType.ExecutionUnknown;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Outcome, "Unknown execution outcome.");
        }

        entry.RecoveryMarker = request.RecoveryMarker;
        entry.MarkerApplied = request.MarkerApplied;

        var evt = await AppendEventAsync(
            entry.OwnerId, entry.Id, entry.OperationId, eventType, request.Actor,
            request.ProviderDetailJson, cancellationToken);
        entry.LastEventSeq = evt.Seq;

        if (opensObservationWindow)
        {
            var windowEvt = await AppendEventAsync(
                entry.OwnerId, entry.Id, entry.OperationId, RecoveryEventType.ObservationWindowOpened,
                request.Actor, detail: null, cancellationToken);
            entry.LastEventSeq = windowEvt.Seq;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<RecoveryLedgerEntry>.Success(entry);
    }

    /// <inheritdoc />
    public async Task<Result<RecoveryLedgerEntry>> RecordObservationAsync(
        RecordObservationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Outcome == RecoveryObservationOutcome.RecurrenceObserved && request.Confidence is null)
        {
            return Result<RecoveryLedgerEntry>.Failure(Error.Validation(
                "RecoveryLedger.ConfidenceRequired",
                "Confidence is required when recording a recurrence observation."));
        }

        using var _ = await AcquireOwnerLockAsync(request.OwnerId, cancellationToken);

        var entry = await _dbContext.RecoveryLedgerEntries
            .FirstOrDefaultAsync(e => e.Id == request.EntryId, cancellationToken);

        if (entry is null || entry.OwnerId != request.OwnerId)
        {
            return Result<RecoveryLedgerEntry>.Failure(Error.NotFound(
                "RecoveryLedger.EntryNotFound", "Recovery ledger entry not found."));
        }

        if (entry.State != RecoveryEntryState.Observing)
        {
            return Result<RecoveryLedgerEntry>.Failure(Error.Conflict(
                "RecoveryLedger.InvalidTransition",
                $"Cannot record an observation for an entry in state '{entry.State}'; expected '{RecoveryEntryState.Observing}'."));
        }

        var now = DateTimeOffset.UtcNow;
        RecoveryEventType eventType;

        switch (request.Outcome)
        {
            case RecoveryObservationOutcome.RecurrenceObserved:
                entry.State = RecoveryEntryState.Returned;
                entry.Disposition = RecoveryDisposition.Returned;
                entry.VerificationResult = VerificationResult.Returned;
                entry.VerificationConfidence = request.Confidence;
                entry.ClosedAt = now;
                eventType = RecoveryEventType.RecurrenceObserved;
                break;

            case RecoveryObservationOutcome.NoRecurrenceObserved:
                entry.State = RecoveryEntryState.Recovered;
                entry.Disposition = RecoveryDisposition.Recovered;
                entry.VerificationResult = VerificationResult.Recovered;
                entry.ClosedAt = now;
                eventType = RecoveryEventType.NoRecurrenceObserved;
                break;

            case RecoveryObservationOutcome.ObservationUnavailable:
                entry.State = RecoveryEntryState.Unverified;
                entry.Disposition = RecoveryDisposition.Unverified;
                entry.VerificationResult = VerificationResult.Unverified;
                entry.ClosedAt = now;
                eventType = RecoveryEventType.ObservationUnavailable;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Outcome, "Unknown observation outcome.");
        }

        var evt = await AppendEventAsync(
            entry.OwnerId, entry.Id, entry.OperationId, eventType, request.Actor,
            request.DetailJson, cancellationToken);
        entry.LastEventSeq = evt.Seq;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<RecoveryLedgerEntry>.Success(entry);
    }

    /// <inheritdoc />
    public async Task<Result<RecoveryLedgerEntry>> SetDispositionAsync(
        Guid entryId, string ownerId, RecoveryActor actor, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result<RecoveryLedgerEntry>.Failure(Error.Validation(
                "RecoveryLedger.ReasonRequired", "A reason is required to write off a recovery ledger entry."));
        }

        using var _ = await AcquireOwnerLockAsync(ownerId, cancellationToken);

        var entry = await _dbContext.RecoveryLedgerEntries
            .FirstOrDefaultAsync(e => e.Id == entryId, cancellationToken);

        if (entry is null || entry.OwnerId != ownerId)
        {
            return Result<RecoveryLedgerEntry>.Failure(Error.NotFound(
                "RecoveryLedger.EntryNotFound", "Recovery ledger entry not found."));
        }

        if (!NonTerminalStates.Contains(entry.State))
        {
            return Result<RecoveryLedgerEntry>.Failure(Error.Conflict(
                "RecoveryLedger.InvalidTransition",
                $"Cannot write off an entry already in terminal state '{entry.State}'."));
        }

        entry.State = RecoveryEntryState.WrittenOff;
        entry.Disposition = RecoveryDisposition.WrittenOff;
        entry.ClosedAt = DateTimeOffset.UtcNow;

        var evt = await AppendEventAsync(
            entry.OwnerId, entry.Id, entry.OperationId, RecoveryEventType.DispositionSet, actor,
            reason, cancellationToken);
        entry.LastEventSeq = evt.Seq;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<RecoveryLedgerEntry>.Success(entry);
    }

    /// <inheritdoc />
    public async Task<Result<RecoveryEvent>> AppendNoteAsync(
        Guid entryId, string ownerId, RecoveryActor actor, string note, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return Result<RecoveryEvent>.Failure(Error.Validation(
                "RecoveryLedger.NoteRequired", "Note text cannot be empty."));
        }

        using var _ = await AcquireOwnerLockAsync(ownerId, cancellationToken);

        var entry = await _dbContext.RecoveryLedgerEntries
            .FirstOrDefaultAsync(e => e.Id == entryId, cancellationToken);

        if (entry is null || entry.OwnerId != ownerId)
        {
            return Result<RecoveryEvent>.Failure(Error.NotFound(
                "RecoveryLedger.EntryNotFound", "Recovery ledger entry not found."));
        }

        var evt = await AppendEventAsync(
            entry.OwnerId, entry.Id, entry.OperationId, RecoveryEventType.OperatorNote, actor,
            note, cancellationToken);
        entry.LastEventSeq = evt.Seq;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<RecoveryEvent>.Success(evt);
    }

    /// <inheritdoc />
    public async Task<ChainVerificationResult> VerifyChainAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var events = await _dbContext.RecoveryEvents
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId)
            .OrderBy(e => e.Seq)
            .ToListAsync(cancellationToken);

        return RecoveryChainVerifier.Verify(ownerId, events);
    }

    /// <inheritdoc />
    public async Task<RecoveryOperation?> GetOperationAsync(Guid operationId, string ownerId, CancellationToken cancellationToken = default)
    {
        var operation = await _dbContext.RecoveryOperations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == operationId, cancellationToken);

        return operation is not null && operation.OwnerId == ownerId ? operation : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecoveryOperation>> QueryOperationsAsync(
        string ownerId, Guid? namespaceId, int limit, CancellationToken cancellationToken = default)
    {
        var operations = _dbContext.RecoveryOperations.AsNoTracking().Where(o => o.OwnerId == ownerId);

        if (namespaceId is { } id)
        {
            operations = operations.Where(o => o.NamespaceId == id);
        }

        return await operations
            .OrderByDescending(o => o.OpenedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecoveryLedgerEntry>> QueryEntriesAsync(
        RecoveryEntryQuery query, CancellationToken cancellationToken = default)
    {
        var entries = _dbContext.RecoveryLedgerEntries.AsNoTracking().Where(e => e.OwnerId == query.OwnerId);

        if (query.OperationId is { } operationId)
        {
            entries = entries.Where(e => e.OperationId == operationId);
        }

        if (query.NamespaceId is { } namespaceId)
        {
            entries = entries.Where(e => e.NamespaceId == namespaceId);
        }

        if (query.DlqMessageId is { } dlqMessageId)
        {
            entries = entries.Where(e => e.DlqMessageId == dlqMessageId);
        }

        if (query.States is { Count: > 0 })
        {
            entries = entries.Where(e => query.States.Contains(e.State));
        }

        return await entries
            .OrderByDescending(e => e.BegunAt)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecoveryLedgerEntry>> GetAgeingAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.RecoveryLedgerEntries
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId && NonTerminalStates.Contains(e.State))
            .OrderBy(e => e.BegunAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RecoveryLedgerEntry?> FindByMarkerAsync(string ownerId, string marker, CancellationToken cancellationToken = default)
    {
        return await _dbContext.RecoveryLedgerEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.OwnerId == ownerId
                                       && e.State == RecoveryEntryState.Observing
                                       && e.RecoveryMarker == marker,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecoveryLedgerEntry>> FindHeuristicRecurrenceCandidatesAsync(
        string ownerId, Guid? namespaceId, string entityName, string bodyHash, DateTimeOffset beganBefore,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.RecoveryLedgerEntries
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId
                        && e.State == RecoveryEntryState.Observing
                        && !e.MarkerApplied
                        && e.NamespaceId == namespaceId
                        && e.EntityNameSnapshot == entityName
                        && e.BodyHash == bodyHash
                        && e.BegunAt < beganBefore)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecoveryEvent>> GetEventsForOperationAsync(
        Guid operationId, string ownerId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.RecoveryEvents
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId && e.OperationId == operationId)
            .OrderBy(e => e.Seq)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> HasAgeingFlagAsync(Guid entryId, string ownerId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.RecoveryEvents
            .AsNoTracking()
            .AnyAsync(e => e.OwnerId == ownerId && e.EntryId == entryId && e.EventType == RecoveryEventType.AgeingFlagged,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<RecoveryLedgerEntry>> FlagAgeingAsync(
        Guid entryId, string ownerId, RecoveryActor actor, int ageInDays, CancellationToken cancellationToken = default)
    {
        using var _ = await AcquireOwnerLockAsync(ownerId, cancellationToken);

        var entry = await _dbContext.RecoveryLedgerEntries
            .FirstOrDefaultAsync(e => e.Id == entryId, cancellationToken);

        if (entry is null || entry.OwnerId != ownerId)
        {
            return Result<RecoveryLedgerEntry>.Failure(Error.NotFound(
                "RecoveryLedger.EntryNotFound", "Recovery ledger entry not found."));
        }

        if (!NonTerminalStates.Contains(entry.State))
        {
            // Already resolved itself — a normal race against verification/interrupted-operation
            // recovery closing the entry between the ageing worker's query and this call. Not an
            // error; nothing left to flag.
            return Result<RecoveryLedgerEntry>.Success(entry);
        }

        var alreadyFlagged = await _dbContext.RecoveryEvents
            .AsNoTracking()
            .AnyAsync(e => e.EntryId == entryId && e.EventType == RecoveryEventType.AgeingFlagged, cancellationToken);

        if (alreadyFlagged)
        {
            // Idempotent: a restarted or overlapping sweep must not double-flag.
            return Result<RecoveryLedgerEntry>.Success(entry);
        }

        var evt = await AppendEventAsync(
            entry.OwnerId, entry.Id, entry.OperationId, RecoveryEventType.AgeingFlagged, actor,
            JsonSerializer.Serialize(new { ageInDays }), cancellationToken);
        entry.LastEventSeq = evt.Seq;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<RecoveryLedgerEntry>.Success(entry);
    }

    /// <inheritdoc />
    public async Task<Result<RecoveryLedgerEntry>> ExpireEntryAsync(
        Guid entryId, string ownerId, RecoveryActor actor, CancellationToken cancellationToken = default)
    {
        using var _ = await AcquireOwnerLockAsync(ownerId, cancellationToken);

        var entry = await _dbContext.RecoveryLedgerEntries
            .FirstOrDefaultAsync(e => e.Id == entryId, cancellationToken);

        if (entry is null || entry.OwnerId != ownerId)
        {
            return Result<RecoveryLedgerEntry>.Failure(Error.NotFound(
                "RecoveryLedger.EntryNotFound", "Recovery ledger entry not found."));
        }

        if (!NonTerminalStates.Contains(entry.State))
        {
            return Result<RecoveryLedgerEntry>.Failure(Error.Conflict(
                "RecoveryLedger.InvalidTransition",
                $"Cannot expire an entry already in terminal state '{entry.State}'."));
        }

        var lastEvent = await _dbContext.RecoveryEvents
            .AsNoTracking()
            .Where(e => e.EntryId == entryId)
            .OrderByDescending(e => e.Seq)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastEvent is null || lastEvent.EventType != RecoveryEventType.AgeingFlagged)
        {
            return Result<RecoveryLedgerEntry>.Failure(Error.Conflict(
                "RecoveryLedger.NotFlagged",
                "An entry can only expire immediately after being flagged by the ageing worker — its most recent event must be AgeingFlagged."));
        }

        entry.State = RecoveryEntryState.Expired;
        entry.Disposition = RecoveryDisposition.Expired;
        entry.ClosedAt = DateTimeOffset.UtcNow;

        var evt = await AppendEventAsync(
            entry.OwnerId, entry.Id, entry.OperationId, RecoveryEventType.DispositionSet, actor,
            detail: "Expired: aged past the ageing threshold with no further recovery activity.", cancellationToken);
        entry.LastEventSeq = evt.Seq;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<RecoveryLedgerEntry>.Success(entry);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecoveryLedgerEntry>> FindLineageMatchesAsync(
        string ownerId, Guid? namespaceId, string entityName, string bodyHash, DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.RecoveryLedgerEntries
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId
                        && e.NamespaceId == namespaceId
                        && e.EntityNameSnapshot == entityName
                        && e.BodyHash == bodyHash
                        && e.BegunAt >= since)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<RecoveryLedgerEntry>> RecordDeclinedAsync(
        BeginRecoveryEntryRequest request, string reasonCode, string? detailJson,
        CancellationToken cancellationToken = default)
    {
        using var _ = await AcquireOwnerLockAsync(request.OwnerId, cancellationToken);

        var operation = await _dbContext.RecoveryOperations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == request.OperationId, cancellationToken);

        if (operation is null || operation.OwnerId != request.OwnerId)
        {
            return Result<RecoveryLedgerEntry>.Failure(Error.NotFound(
                "RecoveryLedger.OperationNotFound", "Recovery operation not found."));
        }

        var now = DateTimeOffset.UtcNow;
        var entry = new RecoveryLedgerEntry
        {
            OperationId = request.OperationId,
            OwnerId = request.OwnerId,
            DlqMessageId = request.DlqMessageId,
            NamespaceId = request.NamespaceId,
            NamespaceNameSnapshot = request.NamespaceNameSnapshot,
            ProviderSnapshot = request.ProviderSnapshot,
            EnvironmentSnapshot = request.EnvironmentSnapshot,
            EntityNameSnapshot = request.EntityNameSnapshot,
            EntityTypeSnapshot = request.EntityTypeSnapshot,
            TopicNameSnapshot = request.TopicNameSnapshot,
            SourceMessageIdSnapshot = request.SourceMessageIdSnapshot,
            SourceSequenceNumberSnapshot = request.SourceSequenceNumberSnapshot,
            BodyHash = request.BodyHash,
            FailureCategorySnapshot = request.FailureCategorySnapshot,
            DeadLetterReasonSnapshot = request.DeadLetterReasonSnapshot,
            SignatureHashSnapshot = request.SignatureHashSnapshot,
            TargetEntity = request.TargetEntity,
            BegunAt = now,
            State = RecoveryEntryState.Declined,
            Disposition = RecoveryDisposition.Declined,
            ClosedAt = now,
        };

        _dbContext.RecoveryLedgerEntries.Add(entry);

        var detail = string.IsNullOrEmpty(detailJson)
            ? JsonSerializer.Serialize(new { reasonCode })
            : detailJson;

        var evt = await AppendEventAsync(
            entry.OwnerId, entry.Id, entry.OperationId,
            RecoveryEventType.EligibilityDeclined, request.Actor, detail, cancellationToken);
        entry.LastEventSeq = evt.Seq;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<RecoveryLedgerEntry>.Success(entry);
    }

    /// <summary>
    /// Appends one event to <paramref name="ownerId"/>'s chain. Must be called while holding
    /// that owner's lock (see <see cref="AcquireOwnerLockAsync"/>) — sequencing considers both
    /// persisted rows and any not-yet-saved <see cref="RecoveryEvent"/> already added to this
    /// unit of work, so a single caller may append more than one event before saving.
    /// </summary>
    private async Task<RecoveryEvent> AppendEventAsync(
        string ownerId, Guid? entryId, Guid operationId, RecoveryEventType eventType,
        RecoveryActor actor, string? detail, CancellationToken cancellationToken)
    {
        var (seq, prevHash) = await GetNextSeqAndPrevHashAsync(ownerId, cancellationToken);
        var occurredAt = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var redactedDetail = detail is null ? null : LogRedactor.Redact(detail);

        var entryHash = RecoveryHashChain.ComputeEntryHash(
            id, ownerId, seq, entryId, operationId, eventType, occurredAt,
            actor.Identity, actor.Kind, redactedDetail, SchemaVersion, prevHash);

        var evt = new RecoveryEvent
        {
            Id = id,
            OwnerId = ownerId,
            Seq = seq,
            EntryId = entryId,
            OperationId = operationId,
            EventType = eventType,
            OccurredAt = occurredAt,
            ActorIdentity = actor.Identity,
            ActorKind = actor.Kind,
            DetailJson = redactedDetail,
            PrevHash = prevHash,
            EntryHash = entryHash,
            SchemaVersion = SchemaVersion,
        };

        _dbContext.RecoveryEvents.Add(evt);
        return evt;
    }

    private async Task<(long NextSeq, string PrevHash)> GetNextSeqAndPrevHashAsync(string ownerId, CancellationToken cancellationToken)
    {
        long? bestSeq = null;
        string? bestHash = null;

        var persistedLast = await _dbContext.RecoveryEvents
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId)
            .OrderByDescending(e => e.Seq)
            .Select(e => new { e.Seq, e.EntryHash })
            .FirstOrDefaultAsync(cancellationToken);

        if (persistedLast is not null)
        {
            bestSeq = persistedLast.Seq;
            bestHash = persistedLast.EntryHash;
        }

        // Also consider events already added to this unit of work but not yet saved — a single
        // ledger call can append more than one event (e.g. RecordExecutionAsync's
        // ProviderAccepted + ObservationWindowOpened) before its one SaveChangesAsync.
        foreach (var tracked in _dbContext.ChangeTracker.Entries<RecoveryEvent>())
        {
            if (tracked.State != EntityState.Added || tracked.Entity.OwnerId != ownerId)
            {
                continue;
            }

            if (bestSeq is null || tracked.Entity.Seq > bestSeq)
            {
                bestSeq = tracked.Entity.Seq;
                bestHash = tracked.Entity.EntryHash;
            }
        }

        return bestSeq is null
            ? (1L, RecoveryHashChain.GenesisHash)
            : (bestSeq.Value + 1, bestHash!);
    }

    private static async Task<IDisposable> AcquireOwnerLockAsync(string ownerId, CancellationToken cancellationToken)
    {
        var semaphore = OwnerLocks.GetOrAdd(ownerId, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new SemaphoreReleaser(semaphore);
    }

    private static string GetServiceVersion()
        => typeof(RecoveryLedgerService).Assembly.GetName().Version?.ToString() ?? "unknown";

    private sealed class SemaphoreReleaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _released;

        public SemaphoreReleaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            _semaphore.Release();
        }
    }
}
