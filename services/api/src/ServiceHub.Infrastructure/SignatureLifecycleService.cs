using System.Collections.Concurrent;
using System.Collections.Immutable;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure;

/// <summary>
/// In-memory implementation of <see cref="ISignatureLifecycleService"/>. State lives in a
/// process-lifetime <see cref="ConcurrentDictionary{TKey,TValue}"/> and is deliberately NOT
/// durable across API restarts — a restart resets every signature back to the default Active
/// status and discards its transition history. This is an accepted MVP limitation (see the
/// interface's XML doc): the schema change needed to persist it durably is out of scope for
/// this release, and this class is the sole implementation callers depend on, so swapping in
/// a durable store later requires no caller changes.
/// </summary>
public sealed class SignatureLifecycleService : ISignatureLifecycleService
{
    private sealed record LifecycleRecord(
        SignatureLifecycleStatus Status,
        SignatureLifecycleStatus? PreviousStatus,
        DateTimeOffset? TransitionedAt,
        string? Notes,
        ImmutableList<SignatureLifecycleEvent> Events);

    private static readonly SignatureLifecycleSnapshot DefaultSnapshot =
        new(SignatureLifecycleStatus.Active, null, null, null);

    // Target status -> set of statuses a signature may transition from to reach it.
    // Active is never a key: no action re-activates a signature directly, only Reopen.
    // Archived is a key but never a source: it is terminal, nothing transitions out of it.
    private static readonly IReadOnlyDictionary<SignatureLifecycleStatus, ISet<SignatureLifecycleStatus>>
        AllowedSourcesByTarget = new Dictionary<SignatureLifecycleStatus, ISet<SignatureLifecycleStatus>>
        {
            [SignatureLifecycleStatus.Resolved] = new HashSet<SignatureLifecycleStatus>
            {
                SignatureLifecycleStatus.Active, SignatureLifecycleStatus.Reopened,
            },
            [SignatureLifecycleStatus.Reopened] = new HashSet<SignatureLifecycleStatus>
            {
                SignatureLifecycleStatus.Resolved, SignatureLifecycleStatus.Suppressed,
            },
            [SignatureLifecycleStatus.Suppressed] = new HashSet<SignatureLifecycleStatus>
            {
                SignatureLifecycleStatus.Active, SignatureLifecycleStatus.Resolved, SignatureLifecycleStatus.Reopened,
            },
            [SignatureLifecycleStatus.Archived] = new HashSet<SignatureLifecycleStatus>
            {
                SignatureLifecycleStatus.Active, SignatureLifecycleStatus.Resolved,
                SignatureLifecycleStatus.Suppressed, SignatureLifecycleStatus.Reopened,
            },
        };

    private readonly ConcurrentDictionary<string, LifecycleRecord> _store = new(StringComparer.Ordinal);
    private readonly object _writeLock = new();

    /// <inheritdoc />
    public Task<Result<SignatureLifecycleSnapshot>> GetStatusAsync(
        string ownerId, Guid namespaceId, string signatureHash, CancellationToken cancellationToken = default)
    {
        var snapshot = _store.TryGetValue(BuildKey(ownerId, namespaceId, signatureHash), out var record)
            ? ToSnapshot(record)
            : DefaultSnapshot;

        return Task.FromResult(Result<SignatureLifecycleSnapshot>.Success(snapshot));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyDictionary<string, SignatureLifecycleSnapshot>>> GetStatusBatchAsync(
        string ownerId, Guid namespaceId, IReadOnlyList<string> signatureHashes, CancellationToken cancellationToken = default)
    {
        var result = signatureHashes.ToDictionary(
            hash => hash,
            hash => _store.TryGetValue(BuildKey(ownerId, namespaceId, hash), out var record)
                ? ToSnapshot(record)
                : DefaultSnapshot);

        return Task.FromResult(Result<IReadOnlyDictionary<string, SignatureLifecycleSnapshot>>.Success(result));
    }

    /// <inheritdoc />
    public Task<Result<SignatureLifecycleSnapshot>> TransitionAsync(
        string ownerId,
        Guid namespaceId,
        string signatureHash,
        SignatureLifecycleStatus targetStatus,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        var key = BuildKey(ownerId, namespaceId, signatureHash);

        lock (_writeLock)
        {
            var current = _store.TryGetValue(key, out var existing)
                ? existing.Status
                : SignatureLifecycleStatus.Active;

            if (!AllowedSourcesByTarget.TryGetValue(targetStatus, out var allowedSources) ||
                !allowedSources.Contains(current))
            {
                return Task.FromResult(Result<SignatureLifecycleSnapshot>.Failure(Error.Validation(
                    "SignatureLifecycle.InvalidTransition",
                    $"Cannot transition a signature from '{current}' to '{targetStatus}'.")));
            }

            var now = DateTimeOffset.UtcNow;
            var events = (_store.TryGetValue(key, out var previousRecord)
                ? previousRecord.Events
                : ImmutableList<SignatureLifecycleEvent>.Empty)
                .Add(new SignatureLifecycleEvent(current, targetStatus, now, notes));

            var updated = new LifecycleRecord(targetStatus, current, now, notes, events);
            _store[key] = updated;

            return Task.FromResult(Result<SignatureLifecycleSnapshot>.Success(ToSnapshot(updated)));
        }
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<SignatureLifecycleEvent>>> GetHistoryAsync(
        string ownerId, Guid namespaceId, string signatureHash, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SignatureLifecycleEvent> events = _store.TryGetValue(
            BuildKey(ownerId, namespaceId, signatureHash), out var record)
            ? record.Events
            : [];

        return Task.FromResult(Result<IReadOnlyList<SignatureLifecycleEvent>>.Success(events));
    }

    private static string BuildKey(string ownerId, Guid namespaceId, string signatureHash) =>
        $"{ownerId}|{namespaceId:N}|{signatureHash}";

    private static SignatureLifecycleSnapshot ToSnapshot(LifecycleRecord record) =>
        new(record.Status, record.PreviousStatus, record.TransitionedAt, record.Notes);
}
