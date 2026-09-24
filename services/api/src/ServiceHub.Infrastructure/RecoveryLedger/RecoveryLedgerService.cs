using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Core.Security;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.RecoveryLedger;

/// <summary>
/// EF Core-backed <see cref="IRecoveryLedger"/> — the sole writer of the Recovery Evidence Ledger. Owns
/// hash-chain sequencing (per owner, serialised by an in-process semaphore and backstopped by the unique
/// <c>(OwnerId, Seq)</c> index) and the entry state machine. Append-only enforcement lives one layer
/// down, in <see cref="RecoveryLedgerAppendOnlyGuard"/>, so a caller other than this class cannot bypass it.
/// </summary>
/// <remarks>
/// Adapted from 4.0.0's 1,875-line service, keeping only the state machine: open, begin, execute,
/// observe, close, verify and the reads those need. Left behind, each for the unit that needs it:
/// declining and ageing (2.6), autonomy grants, production elevation, epochs and rehearsal.
/// </remarks>
public sealed class RecoveryLedgerService : IRecoveryLedger
{
    private const int SchemaVersion = 1;

    /// <summary>The observation window when <c>RecoveryEvidence:ObservationWindowHours</c> is not configured.</summary>
    public const double DefaultObservationWindowHours = 24.0;

    private const double MinObservationWindowHours = 0.1;
    private const double MaxObservationWindowHours = 720.0;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> OwnerLocks = new();

    private static readonly HashSet<RecoveryEntryState> NonTerminalStates =
    [
        RecoveryEntryState.Executing,
        RecoveryEntryState.Observing,
        RecoveryEntryState.ExecutionUnknown,
    ];

    private readonly ServiceHubDbContext _db;
    private readonly double _observationWindowHours;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database.</param>
    /// <param name="configuration">Optional; reads <c>RecoveryEvidence:ObservationWindowHours</c> (clamped to [0.1, 720]).</param>
    public RecoveryLedgerService(ServiceHubDbContext db, IConfiguration? configuration = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _observationWindowHours = ResolveObservationWindowHours(configuration);
    }

    /// <summary>The window a replay will be watched for — one answer for the ledger and for the proposal that promises it.</summary>
    public static double ResolveObservationWindowHours(IConfiguration? configuration) =>
        configuration is null
            ? DefaultObservationWindowHours
            : Math.Clamp(
                configuration.GetValue("RecoveryEvidence:ObservationWindowHours", DefaultObservationWindowHours),
                MinObservationWindowHours, MaxObservationWindowHours);

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

        _db.RecoveryOperations.Add(operation);
        await AppendEventAsync(operation.OwnerId, null, operation.Id, RecoveryEventType.OperationOpened, request.Actor, null, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return Result<RecoveryOperation>.Success(operation);
    }

    /// <inheritdoc />
    public async Task<Result<RecoveryLedgerEntry>> BeginEntryAsync(
        BeginRecoveryEntryRequest request, CancellationToken cancellationToken = default)
    {
        using var _ = await AcquireOwnerLockAsync(request.OwnerId, cancellationToken);

        var operation = await _db.RecoveryOperations.AsNoTracking()
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

        _db.RecoveryLedgerEntries.Add(entry);

        var evt = await AppendEventAsync(entry.OwnerId, entry.Id, entry.OperationId, RecoveryEventType.EntryBegun, request.Actor, null, cancellationToken);
        entry.LastEventSeq = evt.Seq;

        await _db.SaveChangesAsync(cancellationToken);
        return Result<RecoveryLedgerEntry>.Success(entry);
    }

    /// <inheritdoc />
    public async Task<Result<RecoveryLedgerEntry>> RecordExecutionAsync(
        RecordExecutionRequest request, CancellationToken cancellationToken = default)
    {
        using var _ = await AcquireOwnerLockAsync(request.OwnerId, cancellationToken);

        var entry = await _db.RecoveryLedgerEntries.FirstOrDefaultAsync(e => e.Id == request.EntryId, cancellationToken);
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

        var operation = await _db.RecoveryOperations.AsNoTracking()
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
                entry.ObservationWindowEndsAt = now.AddHours(_observationWindowHours);
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

        var evt = await AppendEventAsync(entry.OwnerId, entry.Id, entry.OperationId, eventType, request.Actor, request.ProviderDetailJson, cancellationToken);
        entry.LastEventSeq = evt.Seq;

        if (opensObservationWindow)
        {
            // Every Observing entry has this event, so it is where an auditor looks to answer "what
            // window governed this entry's recurrence check".
            var windowEvt = await AppendEventAsync(
                entry.OwnerId, entry.Id, entry.OperationId, RecoveryEventType.ObservationWindowOpened, request.Actor,
                JsonSerializer.Serialize(new
                {
                    appliedObservationWindowHours = _observationWindowHours,
                    defaultObservationWindowHours = DefaultObservationWindowHours,
                }),
                cancellationToken);
            entry.LastEventSeq = windowEvt.Seq;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result<RecoveryLedgerEntry>.Success(entry);
    }

    /// <inheritdoc />
    public async Task<Result<RecoveryLedgerEntry>> RecordObservationAsync(
        RecordObservationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Outcome == RecoveryObservationOutcome.RecurrenceObserved && request.Confidence is null)
        {
            return Result<RecoveryLedgerEntry>.Failure(Error.Validation(
                "RecoveryLedger.ConfidenceRequired", "Confidence is required when recording a recurrence observation."));
        }

        using var _ = await AcquireOwnerLockAsync(request.OwnerId, cancellationToken);

        var entry = await _db.RecoveryLedgerEntries.FirstOrDefaultAsync(e => e.Id == request.EntryId, cancellationToken);
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
                eventType = RecoveryEventType.RecurrenceObserved;
                break;

            case RecoveryObservationOutcome.NoRecurrenceObserved:
                entry.State = RecoveryEntryState.Recovered;
                entry.Disposition = RecoveryDisposition.Recovered;
                entry.VerificationResult = VerificationResult.Recovered;
                eventType = RecoveryEventType.NoRecurrenceObserved;
                break;

            case RecoveryObservationOutcome.ObservationUnavailable:
                entry.State = RecoveryEntryState.Unverified;
                entry.Disposition = RecoveryDisposition.Unverified;
                entry.VerificationResult = VerificationResult.Unverified;
                eventType = RecoveryEventType.ObservationUnavailable;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Outcome, "Unknown observation outcome.");
        }

        entry.ClosedAt = now;

        var evt = await AppendEventAsync(entry.OwnerId, entry.Id, entry.OperationId, eventType, request.Actor, request.DetailJson, cancellationToken);
        entry.LastEventSeq = evt.Seq;

        await _db.SaveChangesAsync(cancellationToken);
        return Result<RecoveryLedgerEntry>.Success(entry);
    }

    /// <inheritdoc />
    public async Task<Result<RecoveryLedgerEntry>> CloseAsync(
        Guid entryId, string ownerId, RecoveryActor actor, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result<RecoveryLedgerEntry>.Failure(Error.Validation(
                "RecoveryLedger.ReasonRequired", "A reason is required to write off a recovery ledger entry."));
        }

        using var _ = await AcquireOwnerLockAsync(ownerId, cancellationToken);

        var entry = await _db.RecoveryLedgerEntries.FirstOrDefaultAsync(e => e.Id == entryId, cancellationToken);
        if (entry is null || entry.OwnerId != ownerId)
        {
            return Result<RecoveryLedgerEntry>.Failure(Error.NotFound(
                "RecoveryLedger.EntryNotFound", "Recovery ledger entry not found."));
        }

        if (!NonTerminalStates.Contains(entry.State))
        {
            return Result<RecoveryLedgerEntry>.Failure(Error.Conflict(
                "RecoveryLedger.InvalidTransition", $"Cannot write off an entry already in terminal state '{entry.State}'."));
        }

        entry.State = RecoveryEntryState.WrittenOff;
        entry.Disposition = RecoveryDisposition.WrittenOff;
        entry.ClosedAt = DateTimeOffset.UtcNow;

        var evt = await AppendEventAsync(entry.OwnerId, entry.Id, entry.OperationId, RecoveryEventType.DispositionSet, actor, reason, cancellationToken);
        entry.LastEventSeq = evt.Seq;

        await _db.SaveChangesAsync(cancellationToken);
        return Result<RecoveryLedgerEntry>.Success(entry);
    }

    /// <inheritdoc />
    public async Task<ChainVerificationResult> VerifyChainAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        // Epochs are not carried into 4.1.0, so a chain always starts at its own genesis.
        var events = await _db.RecoveryEvents.AsNoTracking()
            .Where(e => e.OwnerId == ownerId)
            .OrderBy(e => e.Seq)
            .ToListAsync(cancellationToken);

        return RecoveryChainVerifier.Verify(ownerId, events);
    }

    /// <inheritdoc />
    public async Task<RecoveryOperation?> GetOperationAsync(Guid operationId, string ownerId, CancellationToken cancellationToken = default)
    {
        var operation = await _db.RecoveryOperations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == operationId, cancellationToken);
        return operation is not null && operation.OwnerId == ownerId ? operation : null;
    }

    /// <inheritdoc />
    public async Task<RecoveryLedgerEntry?> GetEntryAsync(Guid entryId, string ownerId, CancellationToken cancellationToken = default)
    {
        var entry = await _db.RecoveryLedgerEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == entryId, cancellationToken);
        return entry is not null && entry.OwnerId == ownerId ? entry : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecoveryEvent>> GetEventsForOperationAsync(
        Guid operationId, string ownerId, CancellationToken cancellationToken = default) =>
        await _db.RecoveryEvents.AsNoTracking()
            .Where(e => e.OwnerId == ownerId && e.OperationId == operationId)
            .OrderBy(e => e.Seq)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecoveryLedgerEntry>> FindLineageMatchesAsync(
        string ownerId, Guid? namespaceId, string entityName, string bodyHash, DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        // Compared in memory: BegunAt is stored as sortable text, and the lineage set is small (one
        // message's own history), so this stays correct without a provider-specific translation.
        var candidates = await _db.RecoveryLedgerEntries.AsNoTracking()
            .Where(e => e.OwnerId == ownerId && e.NamespaceId == namespaceId
                        && e.EntityNameSnapshot == entityName && e.BodyHash == bodyHash)
            .ToListAsync(cancellationToken);
        return [.. candidates.Where(e => e.BegunAt >= since)];
    }

    /// <inheritdoc />
    public async Task<bool> IsEmergencyStopActiveAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var latest = await _db.RecoveryEvents.AsNoTracking()
            .Where(e => e.OwnerId == ownerId
                && (e.EventType == RecoveryEventType.EmergencyStopActivated
                    || e.EventType == RecoveryEventType.EmergencyStopCleared))
            .OrderByDescending(e => e.Seq)
            .Select(e => e.EventType)
            .FirstOrDefaultAsync(cancellationToken);

        return latest == RecoveryEventType.EmergencyStopActivated;
    }

    /// <inheritdoc />
    public Task<AutonomyGrant?> GetAutonomyGrantAsync(
        string ownerId, string signatureHash, RecoveryOperationKind actionKind, CancellationToken cancellationToken = default) =>
        Task.FromResult<AutonomyGrant?>(null); // no grants exist until unit 4.1

    /// <inheritdoc />
    public Task<ProductionElevation?> GetLiveProductionElevationAsync(
        string ownerId, Guid namespaceId, CancellationToken cancellationToken = default) =>
        Task.FromResult<ProductionElevation?>(null); // 4.1.0 ships no elevation: Prod recovery is denied

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecoveryLedgerEntry>> GetAgeingAsync(
        string ownerId, int limit = int.MaxValue, CancellationToken cancellationToken = default) =>
        await _db.RecoveryLedgerEntries.AsNoTracking()
            .Where(e => e.OwnerId == ownerId && NonTerminalStates.Contains(e.State))
            .OrderBy(e => e.BegunAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<RecoveryLedgerEntry?> FindByMarkerAsync(string ownerId, string marker, CancellationToken cancellationToken = default) =>
        await _db.RecoveryLedgerEntries.AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.OwnerId == ownerId && e.State == RecoveryEntryState.Observing && e.RecoveryMarker == marker,
                cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecoveryLedgerEntry>> FindHeuristicRecurrenceCandidatesAsync(
        string ownerId, Guid? namespaceId, string entityName, string bodyHash, DateTimeOffset beganBefore,
        CancellationToken cancellationToken = default)
    {
        var candidates = await _db.RecoveryLedgerEntries.AsNoTracking()
            .Where(e => e.OwnerId == ownerId && e.State == RecoveryEntryState.Observing && !e.MarkerApplied
                        && e.NamespaceId == namespaceId && e.EntityNameSnapshot == entityName && e.BodyHash == bodyHash)
            .ToListAsync(cancellationToken);
        return [.. candidates.Where(e => e.BegunAt < beganBefore)];
    }

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

        _db.RecoveryEvents.Add(evt);
        return evt;
    }

    private async Task<(long NextSeq, string PrevHash)> GetNextSeqAndPrevHashAsync(string ownerId, CancellationToken cancellationToken)
    {
        long? bestSeq = null;
        string? bestHash = null;

        var persistedLast = await _db.RecoveryEvents.AsNoTracking()
            .Where(e => e.OwnerId == ownerId)
            .OrderByDescending(e => e.Seq)
            .Select(e => new { e.Seq, e.EntryHash })
            .FirstOrDefaultAsync(cancellationToken);

        if (persistedLast is not null)
        {
            bestSeq = persistedLast.Seq;
            bestHash = persistedLast.EntryHash;
        }

        // Events added in this unit of work but not yet saved: one call can append several before its
        // single SaveChanges.
        foreach (var tracked in _db.ChangeTracker.Entries<RecoveryEvent>())
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

        return bestSeq is null ? (1L, RecoveryHashChain.GenesisHash) : (bestSeq.Value + 1, bestHash!);
    }

    private static async Task<IDisposable> AcquireOwnerLockAsync(string ownerId, CancellationToken cancellationToken)
    {
        var semaphore = OwnerLocks.GetOrAdd(ownerId, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new SemaphoreReleaser(semaphore);
    }

    private static string GetServiceVersion()
        => typeof(RecoveryLedgerService).Assembly.GetName().Version?.ToString() ?? "unknown";

    private sealed class SemaphoreReleaser(SemaphoreSlim semaphore) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            semaphore.Release();
        }
    }
}
