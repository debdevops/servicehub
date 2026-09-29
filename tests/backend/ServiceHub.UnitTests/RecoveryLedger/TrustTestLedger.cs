using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.UnitTests.RecoveryLedger;

/// <summary>
/// Test harness for unit 4.1's ported tests (the owner-approved 2.6-style exception, 2026-09-26). 4.0.0's trust and
/// autonomy tests record a <b>declined</b> entry and a person's <b>outcome flag</b> through ledger writes 4.1.0 does
/// not ship. This wraps the REAL 4.1.0 ledger and supplies just those two writes — as real, hash-chained rows in the
/// same tables, shaped exactly as 4.0.0 wrote them — under the method names the tests already call.
/// </summary>
internal sealed class TrustTestLedger(ServiceHubDbContext db)
{
    public RecoveryLedgerService Real { get; } = new(db);

    public Task<Result<RecoveryOperation>> OpenOperationAsync(OpenRecoveryOperationRequest request, CancellationToken cancellationToken = default) => Real.OpenOperationAsync(request, cancellationToken);
    public Task<Result<RecoveryLedgerEntry>> BeginEntryAsync(BeginRecoveryEntryRequest request, CancellationToken cancellationToken = default) => Real.BeginEntryAsync(request, cancellationToken);
    public Task<Result<RecoveryLedgerEntry>> RecordExecutionAsync(RecordExecutionRequest request, CancellationToken cancellationToken = default) => Real.RecordExecutionAsync(request, cancellationToken);
    public Task<Result<RecoveryLedgerEntry>> RecordObservationAsync(RecordObservationRequest request, CancellationToken cancellationToken = default) => Real.RecordObservationAsync(request, cancellationToken);
    public Task<AutonomyGrant?> GetAutonomyGrantAsync(string ownerId, string signatureHash, RecoveryOperationKind actionKind, CancellationToken cancellationToken = default) => Real.GetAutonomyGrantAsync(ownerId, signatureHash, actionKind, cancellationToken);
    public Task<Result<AutonomyGrant>> RecordAutonomyGrantTransitionAsync(string ownerId, string signatureHash, RecoveryOperationKind actionKind, AutonomyLevel previousLevel, AutonomyLevel newLevel, string reason, string? evidenceJson, CancellationToken cancellationToken = default) =>
        Real.RecordAutonomyGrantTransitionAsync(ownerId, signatureHash, actionKind, previousLevel, newLevel, reason, evidenceJson, cancellationToken);

    /// <summary>An entry the gate refused before any cloud was touched: terminal, disposition Declined (4.0.0's shape).</summary>
    public async Task<Result<RecoveryLedgerEntry>> RecordDeclinedAsync(BeginRecoveryEntryRequest request, string reasonCode, string? detailJson, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = new RecoveryLedgerEntry
        {
            OperationId = request.OperationId, OwnerId = request.OwnerId, DlqMessageId = request.DlqMessageId, NamespaceId = request.NamespaceId,
            BodyHash = request.BodyHash, SignatureHashSnapshot = request.SignatureHashSnapshot, TargetEntity = request.TargetEntity,
            BegunAt = now, State = RecoveryEntryState.Declined, Disposition = RecoveryDisposition.Declined, ClosedAt = now,
        };
        db.RecoveryLedgerEntries.Add(entry);
        var seq = await AppendAsync(request.OwnerId, entry.Id, entry.OperationId, RecoveryEventType.EligibilityDeclined, request.Actor,
            string.IsNullOrEmpty(detailJson) ? JsonSerializer.Serialize(new { reasonCode }) : detailJson, cancellationToken);
        entry.LastEventSeq = seq;
        await db.SaveChangesAsync(cancellationToken);
        return Result<RecoveryLedgerEntry>.Success(entry);
    }

    /// <summary>A person's flag on an outcome — the event the trust queries read (<c>{"flagKind": …}</c>).</summary>
    public async Task<Result<RecoveryEvent>> RecordOutcomeFlagAsync(Guid entryId, string ownerId, RecoveryActor actor, RecoveryOutcomeFlagKind flagKind, string reason, CancellationToken cancellationToken = default)
    {
        var entry = await db.RecoveryLedgerEntries.AsNoTracking().FirstAsync(e => e.Id == entryId && e.OwnerId == ownerId, cancellationToken);
        await AppendAsync(ownerId, entryId, entry.OperationId, RecoveryEventType.OutcomeFlagged, actor,
            JsonSerializer.Serialize(new { flagKind = flagKind.ToString(), reason }), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result<RecoveryEvent>.Success(null!);
    }

    private async Task<long> AppendAsync(string ownerId, Guid? entryId, Guid operationId, RecoveryEventType type, RecoveryActor actor, string detail, CancellationToken cancellationToken)
    {
        var last = await db.RecoveryEvents.AsNoTracking().Where(e => e.OwnerId == ownerId).OrderByDescending(e => e.Seq)
            .Select(e => new { e.Seq, e.EntryHash }).FirstOrDefaultAsync(cancellationToken);
        var seq = (last?.Seq ?? 0) + 1;
        var prev = last?.EntryHash ?? RecoveryHashChain.GenesisHash;
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        db.RecoveryEvents.Add(new RecoveryEvent
        {
            Id = id, OwnerId = ownerId, Seq = seq, EntryId = entryId, OperationId = operationId, EventType = type, OccurredAt = now,
            ActorIdentity = actor.Identity, ActorKind = actor.Kind, DetailJson = detail, PrevHash = prev, SchemaVersion = 1,
            EntryHash = RecoveryHashChain.ComputeEntryHash(id, ownerId, seq, entryId, operationId, type, now, actor.Identity, actor.Kind, detail, 1, prev),
        });
        return seq;
    }
}
