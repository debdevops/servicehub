using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

    /// <summary>
    /// The observation window applied when <c>RecoveryEvidence:ObservationWindowHours</c> is not
    /// configured. Also the value <c>ProductionConfigurationValidator</c> compares against
    /// to decide whether a configured value counts as non-default for the startup warning.
    /// </summary>
    public const double DefaultObservationWindowHours = 24.0;

    /// <summary>
    /// The floor <c>RecoveryEvidence:ObservationWindowHours</c> cannot be configured below in
    /// Production (roadmap W1.1, fixes F4) — enforced at startup by
    /// <c>ProductionConfigurationValidator</c>, not by this class, so the floor cannot be bypassed
    /// by constructing this service directly. A shorter window is legitimate for staging,
    /// rehearsal, or CI soak runs, where declaring "no recurrence" faster than 24h is the entire
    /// point; in Production it would let auto-replay declare absence before a re-dead-lettered
    /// message could plausibly reappear.
    /// </summary>
    public const double MinimumProductionObservationWindowHours = 1.0;

    private const double MinObservationWindowHours = 0.1;
    private const double MaxObservationWindowHours = 720.0;

    private static readonly TimeSpan DefaultObservationWindow = TimeSpan.FromHours(DefaultObservationWindowHours);
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
    private readonly INamespaceRepository? _namespaceRepository;
    private readonly double _observationWindowHours;
    private readonly TimeSpan _observationWindow;
    private readonly bool _isNonDefaultObservationWindow;

    /// <summary>
    /// Initialises a new instance of <see cref="RecoveryLedgerService"/>.
    /// </summary>
    /// <param name="dbContext">The DLQ database context.</param>
    /// <param name="configuration">
    /// Application configuration — reads <c>RecoveryEvidence:ObservationWindowHours</c> (default
    /// <see cref="DefaultObservationWindowHours"/>, clamped to [0.1, 720]). Optional and defaults
    /// to <see langword="null"/> so existing callers that construct this service directly (tests,
    /// mainly) keep the exact hardcoded-24h behaviour this class always had; DI-resolved instances
    /// always receive the real <see cref="IConfiguration"/>. The Production floor
    /// (<see cref="MinimumProductionObservationWindowHours"/>) is enforced separately, at startup,
    /// by <c>ProductionConfigurationValidator</c> — this constructor does not re-check it, so a
    /// value below the floor still clamps to <see cref="MinObservationWindowHours"/>/<see cref="MaxObservationWindowHours"/>
    /// here rather than failing, exactly like every other worker in this codebase that reads
    /// <c>RecoveryEvidence:*</c>.
    /// </param>
    /// <param name="namespaceRepository">
    /// Used only as a fallback in <see cref="GetSignatureProviderAsync"/> when a signature has
    /// never had a recovery ledger entry written for it yet (so no <c>ProviderSnapshot</c> exists)
    /// — resolves the provider from the namespace the signature was last observed in instead.
    /// Optional and defaults to <see langword="null"/> so existing direct-construction callers
    /// (tests, mainly) are unaffected; DI-resolved instances always receive the real repository.
    /// </param>
    public RecoveryLedgerService(
        DlqDbContext dbContext, IConfiguration? configuration = null, INamespaceRepository? namespaceRepository = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _namespaceRepository = namespaceRepository;

        _observationWindowHours = configuration is null
            ? DefaultObservationWindowHours
            : Math.Clamp(
                configuration.GetValue("RecoveryEvidence:ObservationWindowHours", DefaultObservationWindowHours),
                MinObservationWindowHours, MaxObservationWindowHours);
        _observationWindow = TimeSpan.FromHours(_observationWindowHours);
        _isNonDefaultObservationWindow = _observationWindowHours != DefaultObservationWindowHours;
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
                entry.ObservationWindowEndsAt = now.Add(_observationWindow);
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
            // Always carries the applied window, not only when non-default: this is the one
            // event every Observing entry is guaranteed to have, so it is where an auditor looks
            // first to answer "what window governed this entry's recurrence check" — the
            // evidence export's per-entry summary has no dedicated column for it (roadmap W1.1).
            var windowDetail = JsonSerializer.Serialize(new
            {
                appliedObservationWindowHours = _observationWindowHours,
                defaultObservationWindowHours = DefaultObservationWindowHours,
            });

            var windowEvt = await AppendEventAsync(
                entry.OwnerId, entry.Id, entry.OperationId, RecoveryEventType.ObservationWindowOpened,
                request.Actor, windowDetail, cancellationToken);
            entry.LastEventSeq = windowEvt.Seq;

            if (_isNonDefaultObservationWindow)
            {
                // A second, distinct event on top of ObservationWindowOpened above — so a
                // non-default window is individually queryable/countable across the ledger
                // (roadmap W1.1's "an audit event on every non-default value"), not just
                // recoverable by parsing every ObservationWindowOpened's DetailJson.
                var nonDefaultEvt = await AppendEventAsync(
                    entry.OwnerId, entry.Id, entry.OperationId,
                    RecoveryEventType.NonDefaultObservationWindowApplied, request.Actor,
                    windowDetail, cancellationToken);
                entry.LastEventSeq = nonDefaultEvt.Seq;
            }
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
    public async Task<Result<RecoveryEvent>> RecordRecurrenceContextAsync(
        Guid entryId, string ownerId, RecoveryActor actor, string reasonCode, int matchedCount,
        CancellationToken cancellationToken = default)
    {
        using var _ = await AcquireOwnerLockAsync(ownerId, cancellationToken);

        var entry = await _dbContext.RecoveryLedgerEntries
            .FirstOrDefaultAsync(e => e.Id == entryId, cancellationToken);

        if (entry is null || entry.OwnerId != ownerId)
        {
            return Result<RecoveryEvent>.Failure(Error.NotFound(
                "RecoveryLedger.EntryNotFound", "Recovery ledger entry not found."));
        }

        var detail = JsonSerializer.Serialize(new { reasonCode, matchedCount });

        var evt = await AppendEventAsync(
            entry.OwnerId, entry.Id, entry.OperationId, RecoveryEventType.RecurrenceCapObserved, actor,
            detail, cancellationToken);
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
    public async Task<RecoveryLedgerEntry?> GetEntryAsync(Guid entryId, string ownerId, CancellationToken cancellationToken = default)
    {
        var entry = await _dbContext.RecoveryLedgerEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entryId, cancellationToken);

        return entry is not null && entry.OwnerId == ownerId ? entry : null;
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
    public async Task<IReadOnlyDictionary<Guid, int>> GetEntryCountsAsync(
        IReadOnlyCollection<Guid> operationIds, string ownerId, CancellationToken cancellationToken = default)
    {
        if (operationIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var counts = await _dbContext.RecoveryLedgerEntries.AsNoTracking()
            .Where(e => e.OwnerId == ownerId && operationIds.Contains(e.OperationId))
            .GroupBy(e => e.OperationId)
            .Select(g => new { OperationId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(c => c.OperationId, c => c.Count);
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
    public async Task<IReadOnlyList<RecoveryLedgerEntry>> GetAgeingAsync(
        string ownerId, int limit = int.MaxValue, CancellationToken cancellationToken = default)
    {
        return await _dbContext.RecoveryLedgerEntries
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId && NonTerminalStates.Contains(e.State))
            .OrderBy(e => e.BegunAt)
            .Take(limit)
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
    public async Task<IReadOnlyList<RecoveryLedgerEntry>> FindEntriesForEntitySinceAsync(
        string ownerId, Guid? namespaceId, string entityName, DateTimeOffset since, int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.RecoveryLedgerEntries
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId
                        && e.NamespaceId == namespaceId
                        && e.EntityNameSnapshot == entityName
                        && e.BegunAt >= since)
            .OrderBy(e => e.BegunAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecoveryLedgerEntry>> FindEntriesForSignatureSinceAsync(
        string ownerId, string signatureHash, DateTimeOffset since, int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.RecoveryLedgerEntries
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId
                        && e.SignatureHashSnapshot == signatureHash
                        && e.BegunAt >= since)
            .OrderBy(e => e.BegunAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<RecoveryDisposition, int>> GetDispositionCountsAsync(
        string ownerId, string signatureHash, RecoveryOperationKind actionKind,
        CancellationToken cancellationToken = default)
    {
        var counts = await _dbContext.RecoveryLedgerEntries
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId
                        && e.SignatureHashSnapshot == signatureHash
                        && e.Disposition != null)
            .Join(
                _dbContext.RecoveryOperations.AsNoTracking().Where(o => o.Kind == actionKind),
                e => e.OperationId,
                o => o.Id,
                (e, _) => e.Disposition!.Value)
            .GroupBy(disposition => disposition)
            .Select(g => new { Disposition = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(x => x.Disposition, x => x.Count);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetDistinctSignatureHashesAsync(
        string ownerId, RecoveryOperationKind actionKind, int limit = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.RecoveryLedgerEntries
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId && e.SignatureHashSnapshot != null)
            .Join(
                _dbContext.RecoveryOperations.AsNoTracking().Where(o => o.Kind == actionKind),
                e => e.OperationId,
                o => o.Id,
                (e, _) => e.SignatureHashSnapshot!)
            .Distinct()
            .OrderBy(hash => hash)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CloudProviderType?> GetSignatureProviderAsync(
        string ownerId, string signatureHash, CancellationToken cancellationToken = default)
    {
        var fromLedger = await _dbContext.RecoveryLedgerEntries
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId
                        && e.SignatureHashSnapshot == signatureHash
                        && e.ProviderSnapshot != null)
            .Select(e => e.ProviderSnapshot)
            .FirstOrDefaultAsync(cancellationToken);

        if (fromLedger is not null || _namespaceRepository is null)
        {
            return fromLedger;
        }

        // A signature that has never had a replay/purge recorded against it has no
        // ProviderSnapshot in the ledger yet — falling back to null here (and letting callers
        // fail closed to AWS's stricter capabilities) wrongly tells an operator a
        // never-yet-replayed Azure signature can't do something Azure actually can. Resolve the
        // provider from the namespace the signature was last (or most recently) observed in
        // instead — NamespaceSignature rows exist independently of the recovery ledger.
        var namespaceId = await _dbContext.NamespaceSignatures
            .AsNoTracking()
            .Where(s => s.OwnerId == ownerId && s.SignatureHash == signatureHash)
            .OrderByDescending(s => s.LastSeenAt)
            .Select(s => (Guid?)s.NamespaceId)
            .FirstOrDefaultAsync(cancellationToken);

        if (namespaceId is null)
        {
            return null;
        }

        var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId.Value, cancellationToken);
        return namespaceResult.IsSuccess ? namespaceResult.Value.Provider : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecoveryDisposition>> GetRecentVerifiedDispositionsAsync(
        string ownerId, string signatureHash, RecoveryOperationKind actionKind, int count,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.RecoveryLedgerEntries
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId
                        && e.SignatureHashSnapshot == signatureHash
                        && (e.Disposition == RecoveryDisposition.Recovered || e.Disposition == RecoveryDisposition.Returned))
            .Join(
                _dbContext.RecoveryOperations.AsNoTracking().Where(o => o.Kind == actionKind),
                e => e.OperationId,
                o => o.Id,
                (e, _) => e)
            .OrderByDescending(e => e.LastEventSeq)
            .Take(count)
            .Select(e => e.Disposition!.Value)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecoveryDisposition>> GetRecentVerifiedDispositionsByRuleAsync(
        string ownerId, long ruleId, RecoveryOperationKind actionKind, int count,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.RecoveryLedgerEntries
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId
                        && (e.Disposition == RecoveryDisposition.Recovered || e.Disposition == RecoveryDisposition.Returned))
            .Join(
                _dbContext.RecoveryOperations.AsNoTracking()
                    .Where(o => o.Kind == actionKind && o.SourceRuleId == ruleId),
                e => e.OperationId,
                o => o.Id,
                (e, _) => e)
            .OrderByDescending(e => e.LastEventSeq)
            .Take(count)
            .Select(e => e.Disposition!.Value)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<RecoveryOperation>> RecordAutoReplayCircuitBreakerTripAsync(
        string ownerId, long ruleId, string ruleName, RecoveryActor actor, int sampleSize,
        double verifiedSuccessRate, double appliedSuccessRateFloor,
        CancellationToken cancellationToken = default)
    {
        using var _ = await AcquireOwnerLockAsync(ownerId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var operation = new RecoveryOperation
        {
            OwnerId = ownerId,
            Kind = RecoveryOperationKind.AutoReplayRuleControl,
            Trigger = RecoveryTrigger.AutoReplayCircuitBreaker,
            ActorIdentity = actor.Identity,
            ActorKind = actor.Kind,
            ActorScopes = actor.Scopes,
            Reason = $"Verified success rate {verifiedSuccessRate:P0} over last {sampleSize} outcomes fell below the {appliedSuccessRateFloor:P0} circuit-breaker floor",
            NamespaceId = null,
            SourceRuleId = ruleId,
            ScopeDescription = $"auto-replay rule {ruleId} ({ruleName}) circuit breaker",
            ServiceVersion = GetServiceVersion(),
            OpenedAt = now,
            TargetCount = 0,
        };
        _dbContext.RecoveryOperations.Add(operation);

        var detail = JsonSerializer.Serialize(new
        {
            ruleId,
            ruleName,
            sampleSize,
            verifiedSuccessRate,
            appliedSuccessRateFloor,
            defaultSuccessRateFloor = BackgroundServices.AutonomyEvaluationWorker.DefaultCircuitBreakerSuccessRateFloor,
        });

        await AppendEventAsync(
            ownerId, entryId: null, operation.Id, RecoveryEventType.AutoReplayRuleCircuitBreakerTripped,
            actor, detail, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<RecoveryOperation>.Success(operation);
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

    /// <inheritdoc />
    public async Task<Result<AutonomyGrant>> RecordAutonomyGrantTransitionAsync(
        string ownerId, string signatureHash, RecoveryOperationKind actionKind,
        AutonomyLevel previousLevel, AutonomyLevel newLevel, string reason, string? evidenceJson,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result<AutonomyGrant>.Failure(Error.Validation(
                "RecoveryLedger.ReasonRequired", "A reason is required to record an autonomy grant transition."));
        }

        if (previousLevel == newLevel)
        {
            return Result<AutonomyGrant>.Failure(Error.Validation(
                "RecoveryLedger.NotATransition", "previousLevel and newLevel must differ."));
        }

        using var _ = await AcquireOwnerLockAsync(ownerId, cancellationToken);

        var grant = await _dbContext.AutonomyGrants.FirstOrDefaultAsync(
            g => g.OwnerId == ownerId && g.SignatureHash == signatureHash && g.ActionKind == actionKind,
            cancellationToken);

        var now = DateTimeOffset.UtcNow;

        if (grant is null)
        {
            grant = new AutonomyGrant
            {
                OwnerId = ownerId,
                SignatureHash = signatureHash,
                ActionKind = actionKind,
                CurrentLevel = newLevel,
                UpdatedAtUtc = now,
            };
            _dbContext.AutonomyGrants.Add(grant);
        }
        else
        {
            if (grant.CurrentLevel != previousLevel)
            {
                // A losing snapshot race between two independent callers deciding a transition
                // from the same stale read (e.g. the hourly sweep and an event-time fast-demotion
                // check) — not a caller error. The winner already applied a transition; re-applying
                // this one would log a duplicate forensic event against a previousLevel that no
                // longer reflects the grant's real history.
                return Result<AutonomyGrant>.Failure(Error.Conflict(
                    "RecoveryLedger.StaleAutonomyGrantTransition",
                    $"AutonomyGrant for signature {signatureHash} is currently at {grant.CurrentLevel}, not the expected {previousLevel}; another writer already transitioned it."));
            }

            grant.CurrentLevel = newLevel;
            grant.UpdatedAtUtc = now;
        }

        var actor = ActorIdentityResolver.ResolveSystemActor("AutonomyEvaluationWorker");

        var operation = new RecoveryOperation
        {
            OwnerId = ownerId,
            Kind = RecoveryOperationKind.AutonomyGrantChange,
            Trigger = RecoveryTrigger.AutonomyEvaluation,
            ActorIdentity = actor.Identity,
            ActorKind = actor.Kind,
            ActorScopes = actor.Scopes,
            NamespaceId = null,
            ScopeDescription = $"signature={signatureHash}; action={actionKind}",
            ServiceVersion = GetServiceVersion(),
            OpenedAt = now,
            TargetCount = 0,
        };
        _dbContext.RecoveryOperations.Add(operation);

        var evidence = evidenceJson is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(evidenceJson);
        var detail = JsonSerializer.Serialize(new
        {
            signatureHash,
            actionKind = actionKind.ToString(),
            previousLevel = previousLevel.ToString(),
            newLevel = newLevel.ToString(),
            reason,
            evidence,
        });

        var eventType = newLevel > previousLevel
            ? RecoveryEventType.AutonomyGrantPromoted
            : RecoveryEventType.AutonomyGrantDemoted;

        await AppendEventAsync(ownerId, entryId: null, operation.Id, eventType, actor, detail, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<AutonomyGrant>.Success(grant);
    }

    /// <inheritdoc />
    public async Task<AutonomyGrant?> GetAutonomyGrantAsync(
        string ownerId, string signatureHash, RecoveryOperationKind actionKind,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.AutonomyGrants
            .AsNoTracking()
            .FirstOrDefaultAsync(
                g => g.OwnerId == ownerId && g.SignatureHash == signatureHash && g.ActionKind == actionKind,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsEmergencyStopActiveAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var latest = await _dbContext.RecoveryEvents
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId
                && (e.EventType == RecoveryEventType.EmergencyStopActivated
                    || e.EventType == RecoveryEventType.EmergencyStopCleared))
            .OrderByDescending(e => e.Seq)
            .Select(e => e.EventType)
            .FirstOrDefaultAsync(cancellationToken);

        return latest == RecoveryEventType.EmergencyStopActivated;
    }

    /// <inheritdoc />
    public async Task<Result<RecoveryOperation>> RecordEmergencyControlEventAsync(
        string ownerId, RecoveryActor actor, bool activate, string? reason,
        CancellationToken cancellationToken = default)
    {
        using var _ = await AcquireOwnerLockAsync(ownerId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var operation = new RecoveryOperation
        {
            OwnerId = ownerId,
            Kind = RecoveryOperationKind.EmergencyControl,
            Trigger = RecoveryTrigger.EmergencyControl,
            ActorIdentity = actor.Identity,
            ActorKind = actor.Kind,
            ActorScopes = actor.Scopes,
            Reason = reason,
            NamespaceId = null,
            ScopeDescription = activate ? "emergency-stop=activate" : "emergency-stop=clear",
            ServiceVersion = GetServiceVersion(),
            OpenedAt = now,
            TargetCount = 0,
        };
        _dbContext.RecoveryOperations.Add(operation);

        var eventType = activate ? RecoveryEventType.EmergencyStopActivated : RecoveryEventType.EmergencyStopCleared;
        var detail = JsonSerializer.Serialize(new { reason });

        await AppendEventAsync(ownerId, entryId: null, operation.Id, eventType, actor, detail, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<RecoveryOperation>.Success(operation);
    }

    /// <inheritdoc />
    public async Task<Result<RecoveryEvent>> RecordOutcomeFlagAsync(
        Guid entryId, string ownerId, RecoveryActor actor, RecoveryOutcomeFlagKind flagKind, string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result<RecoveryEvent>.Failure(Error.Validation(
                "RecoveryLedger.OutcomeFlagReasonRequired", "A reason is required to flag a recovery outcome."));
        }

        using var _ = await AcquireOwnerLockAsync(ownerId, cancellationToken);

        var entry = await _dbContext.RecoveryLedgerEntries
            .FirstOrDefaultAsync(e => e.Id == entryId, cancellationToken);

        if (entry is null || entry.OwnerId != ownerId)
        {
            return Result<RecoveryEvent>.Failure(Error.NotFound(
                "RecoveryLedger.EntryNotFound", "Recovery ledger entry not found."));
        }

        var detail = JsonSerializer.Serialize(new { flagKind = flagKind.ToString(), reason });

        var evt = await AppendEventAsync(
            entry.OwnerId, entry.Id, entry.OperationId, RecoveryEventType.OutcomeFlagged, actor,
            detail, cancellationToken);
        entry.LastEventSeq = evt.Seq;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<RecoveryEvent>.Success(evt);
    }

    /// <inheritdoc />
    public async Task<bool> HasUnsafeOutcomeFlagAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var detailJsonValues = await _dbContext.RecoveryEvents
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId && e.EventType == RecoveryEventType.OutcomeFlagged)
            .Select(e => e.DetailJson)
            .ToListAsync(cancellationToken);

        return detailJsonValues.Any(json => TryParseFlagKind(json) == RecoveryOutcomeFlagKind.Unsafe);
    }

    /// <inheritdoc />
    public async Task<bool> HasDuplicateAssociationAsync(
        string ownerId, string signatureHash, CancellationToken cancellationToken = default)
    {
        var detailJsonValues = await _dbContext.RecoveryEvents
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId && e.EventType == RecoveryEventType.OutcomeFlagged && e.EntryId != null)
            .Join(
                _dbContext.RecoveryLedgerEntries.AsNoTracking().Where(entry => entry.SignatureHashSnapshot == signatureHash),
                e => e.EntryId,
                entry => entry.Id,
                (e, _) => e.DetailJson)
            .ToListAsync(cancellationToken);

        return detailJsonValues.Any(json => TryParseFlagKind(json) == RecoveryOutcomeFlagKind.DuplicateBusinessEffect);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AutonomyGrant>> GetAutonomyGrantsAsync(
        string ownerId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.AutonomyGrants
            .AsNoTracking()
            .Where(g => g.OwnerId == ownerId)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AutonomyTransitionRecord>> GetRecentAutonomyTransitionsAsync(
        string ownerId, int limit, CancellationToken cancellationToken = default)
    {
        var events = await _dbContext.RecoveryEvents
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId
                && (e.EventType == RecoveryEventType.AutonomyGrantPromoted
                    || e.EventType == RecoveryEventType.AutonomyGrantDemoted))
            .OrderByDescending(e => e.Seq)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(cancellationToken);

        var results = new List<AutonomyTransitionRecord>(events.Count);
        foreach (var evt in events)
        {
            if (TryParseTransitionDetail(evt.DetailJson) is { } transition)
            {
                results.Add(transition with { OccurredAtUtc = evt.OccurredAt });
            }
        }

        return results;
    }

    private static AutonomyTransitionRecord? TryParseTransitionDetail(string? detailJson)
    {
        if (string.IsNullOrEmpty(detailJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(detailJson);
            var root = document.RootElement;

            if (!root.TryGetProperty("signatureHash", out var signatureHashProp)
                || !root.TryGetProperty("actionKind", out var actionKindProp)
                || !root.TryGetProperty("previousLevel", out var previousLevelProp)
                || !root.TryGetProperty("newLevel", out var newLevelProp)
                || !root.TryGetProperty("reason", out var reasonProp))
            {
                return null;
            }

            if (!Enum.TryParse<RecoveryOperationKind>(actionKindProp.GetString(), out var actionKind)
                || !Enum.TryParse<AutonomyLevel>(previousLevelProp.GetString(), out var previousLevel)
                || !Enum.TryParse<AutonomyLevel>(newLevelProp.GetString(), out var newLevel))
            {
                return null;
            }

            var signatureHash = signatureHashProp.GetString();
            var reason = reasonProp.GetString();
            if (signatureHash is null || reason is null)
            {
                return null;
            }

            return new AutonomyTransitionRecord(
                signatureHash, actionKind, previousLevel, newLevel, reason, OccurredAtUtc: default);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static RecoveryOutcomeFlagKind? TryParseFlagKind(string? detailJson)
    {
        if (detailJson is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(detailJson);
        return document.RootElement.TryGetProperty("flagKind", out var value)
            && Enum.TryParse<RecoveryOutcomeFlagKind>(value.GetString(), out var flagKind)
            ? flagKind
            : null;
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
