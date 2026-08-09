using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure;

/// <summary>
/// EF Core-backed implementation of <see cref="ISignatureLifecycleService"/>. Current status
/// lives in <see cref="DlqDbContext.SignatureLifecycleStates"/> (one row per signature, upserted
/// on each transition) and full transition history in
/// <see cref="DlqDbContext.SignatureLifecycleHistory"/> (append-only), so both survive an API
/// restart. A signature with no state row is implicitly <c>Active</c> — the same default the
/// prior in-memory implementation used, so no data backfill is needed.
/// </summary>
public sealed class SignatureLifecycleService : ISignatureLifecycleService
{
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

    private readonly DlqDbContext _dbContext;
    private readonly IAuditService _auditService;

    /// <summary>Initialises a new instance of <see cref="SignatureLifecycleService"/>.</summary>
    public SignatureLifecycleService(DlqDbContext dbContext, IAuditService auditService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
    }

    /// <inheritdoc />
    public async Task<Result<SignatureLifecycleSnapshot>> GetStatusAsync(
        string ownerId, Guid namespaceId, string signatureHash, CancellationToken cancellationToken = default)
    {
        var state = await _dbContext.SignatureLifecycleStates
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.OwnerId == ownerId && s.NamespaceId == namespaceId && s.SignatureHash == signatureHash,
                cancellationToken);

        return Result<SignatureLifecycleSnapshot>.Success(state is null ? DefaultSnapshot : ToSnapshot(state));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyDictionary<string, SignatureLifecycleSnapshot>>> GetStatusBatchAsync(
        string ownerId, Guid namespaceId, IReadOnlyList<string> signatureHashes, CancellationToken cancellationToken = default)
    {
        var states = await _dbContext.SignatureLifecycleStates
            .AsNoTracking()
            .Where(s => s.OwnerId == ownerId && s.NamespaceId == namespaceId && signatureHashes.Contains(s.SignatureHash))
            .ToDictionaryAsync(s => s.SignatureHash, cancellationToken);

        IReadOnlyDictionary<string, SignatureLifecycleSnapshot> result = signatureHashes.ToDictionary(
            hash => hash,
            hash => states.TryGetValue(hash, out var state) ? ToSnapshot(state) : DefaultSnapshot);

        return Result<IReadOnlyDictionary<string, SignatureLifecycleSnapshot>>.Success(result);
    }

    /// <inheritdoc />
    public async Task<Result<SignatureLifecycleSnapshot>> TransitionAsync(
        string ownerId,
        Guid namespaceId,
        string signatureHash,
        SignatureLifecycleStatus targetStatus,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.SignatureLifecycleStates.FirstOrDefaultAsync(
            s => s.OwnerId == ownerId && s.NamespaceId == namespaceId && s.SignatureHash == signatureHash,
            cancellationToken);

        var current = existing?.Status ?? SignatureLifecycleStatus.Active;

        if (!AllowedSourcesByTarget.TryGetValue(targetStatus, out var allowedSources) ||
            !allowedSources.Contains(current))
        {
            return Result<SignatureLifecycleSnapshot>.Failure(Error.Validation(
                "SignatureLifecycle.InvalidTransition",
                $"Cannot transition a signature from '{current}' to '{targetStatus}'."));
        }

        var now = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            existing = new SignatureLifecycleState
            {
                OwnerId = ownerId,
                NamespaceId = namespaceId,
                SignatureHash = signatureHash,
                Status = targetStatus,
                PreviousStatus = current,
                TransitionedAt = now,
                Notes = notes,
                CreatedAt = now,
            };
            _dbContext.SignatureLifecycleStates.Add(existing);
        }
        else
        {
            existing.PreviousStatus = current;
            existing.Status = targetStatus;
            existing.TransitionedAt = now;
            existing.Notes = notes;
        }

        _dbContext.SignatureLifecycleHistory.Add(new SignatureLifecycleHistoryEntry
        {
            OwnerId = ownerId,
            NamespaceId = namespaceId,
            SignatureHash = signatureHash,
            FromStatus = current,
            ToStatus = targetStatus,
            Timestamp = now,
            Notes = notes,
        });

        _auditService.Enqueue(new AuditLog
        {
            Id = Guid.NewGuid(),
            Timestamp = now,
            OwnerId = ownerId,
            UserIdentity = ownerId,
            Action = "SignatureLifecycle.Transition",
            Outcome = targetStatus.ToString(),
            NamespaceId = namespaceId,
            ResourceName = signatureHash,
            DetailsJson = JsonSerializer.Serialize(new { fromStatus = current.ToString(), toStatus = targetStatus.ToString(), notes }),
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<SignatureLifecycleSnapshot>.Success(ToSnapshot(existing));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SignatureLifecycleEvent>>> GetHistoryAsync(
        string ownerId, Guid namespaceId, string signatureHash, CancellationToken cancellationToken = default)
    {
        var events = await _dbContext.SignatureLifecycleHistory
            .AsNoTracking()
            .Where(h => h.OwnerId == ownerId && h.NamespaceId == namespaceId && h.SignatureHash == signatureHash)
            .OrderBy(h => h.Timestamp)
            .Select(h => new SignatureLifecycleEvent(h.FromStatus, h.ToStatus, h.Timestamp, h.Notes))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<SignatureLifecycleEvent>>.Success(events);
    }

    private static SignatureLifecycleSnapshot ToSnapshot(SignatureLifecycleState state) =>
        new(state.Status, state.PreviousStatus, state.TransitionedAt, state.Notes);
}
