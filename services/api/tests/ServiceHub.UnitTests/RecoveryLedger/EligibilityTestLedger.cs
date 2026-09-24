using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.UnitTests.RecoveryLedger;

/// <summary>
/// Test harness for the eligibility gate's ported tests. 4.0.0's tests drove elevation, autonomy grants
/// and emergency stop through its full ledger; 4.1.0's ledger carries none of them yet (elevation is not
/// shipped, autonomy is unit 4.1, emergency stop is 6.10). This wraps the REAL 4.1.0 ledger — lineage,
/// events, entries all real — and supplies just those three external states in memory, under the
/// method names and signatures the tests already call, so no assertion had to change.
/// </summary>
internal sealed class EligibilityTestLedger : IRecoveryLedger
{
    private readonly ServiceHubDbContext _db;
    private readonly RecoveryLedgerService _real;
    private readonly List<ProductionElevation> _elevations = [];
    private readonly Dictionary<(string Owner, string Signature, RecoveryOperationKind Kind), AutonomyGrant> _grants = [];

    public EligibilityTestLedger(ServiceHubDbContext db)
    {
        _db = db;
        _real = new RecoveryLedgerService(db);
    }

    // ── What 4.0.0's ledger offered and the tests call ─────────────────────────

    public async Task<Result<RecoveryOperation>> RecordEmergencyControlEventAsync(
        string ownerId, RecoveryActor actor, bool activate, string? reason, CancellationToken cancellationToken = default)
    {
        var operationId = Guid.NewGuid();
        var last = _db.RecoveryEvents.Where(e => e.OwnerId == ownerId).OrderByDescending(e => e.Seq)
            .Select(e => new { e.Seq, e.EntryHash }).FirstOrDefault();
        var seq = (last?.Seq ?? 0) + 1;
        var prev = last?.EntryHash ?? RecoveryHashChain.GenesisHash;
        var type = activate ? RecoveryEventType.EmergencyStopActivated : RecoveryEventType.EmergencyStopCleared;
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        _db.RecoveryEvents.Add(new RecoveryEvent
        {
            Id = id, OwnerId = ownerId, Seq = seq, EntryId = null, OperationId = operationId, EventType = type,
            OccurredAt = now, ActorIdentity = actor.Identity, ActorKind = actor.Kind, DetailJson = reason,
            PrevHash = prev, SchemaVersion = 1,
            EntryHash = RecoveryHashChain.ComputeEntryHash(id, ownerId, seq, null, operationId, type, now, actor.Identity, actor.Kind, reason, 1, prev),
        });
        await _db.SaveChangesAsync(cancellationToken);
        return Result<RecoveryOperation>.Success(null!);
    }

    public Task<Result<AutonomyGrant>> RecordAutonomyGrantTransitionAsync(
        string ownerId, string signatureHash, RecoveryOperationKind actionKind, AutonomyLevel previousLevel,
        AutonomyLevel newLevel, string reason, string? evidenceJson, CancellationToken cancellationToken = default)
    {
        var grant = new AutonomyGrant { OwnerId = ownerId, SignatureHash = signatureHash, ActionKind = actionKind, CurrentLevel = newLevel, UpdatedAtUtc = DateTimeOffset.UtcNow };
        _grants[(ownerId, signatureHash, actionKind)] = grant;
        return Task.FromResult(Result<AutonomyGrant>.Success(grant));
    }

    public Task<Result<ProductionElevation>> RequestProductionElevationAsync(
        string ownerId, Guid namespaceId, string? namespaceNameSnapshot, RecoveryActor actor, string reason, TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var elevation = new ProductionElevation
        {
            OwnerId = ownerId, NamespaceId = namespaceId, NamespaceNameSnapshot = namespaceNameSnapshot, Reason = reason,
            RequestedByIdentity = actor.Identity, RequestedAt = DateTimeOffset.UtcNow, RequestedDuration = duration,
        };
        _elevations.Add(elevation);
        return Task.FromResult(Result<ProductionElevation>.Success(elevation));
    }

    public Task<Result<ProductionElevation>> ApproveProductionElevationAsync(
        Guid elevationId, string ownerId, RecoveryActor actor, CancellationToken cancellationToken = default)
    {
        var e = _elevations.Single(x => x.Id == elevationId && x.OwnerId == ownerId);
        e.ApprovedByIdentity = actor.Identity;
        e.ApprovedAt = DateTimeOffset.UtcNow;
        e.ExpiresAt = e.ApprovedAt + e.RequestedDuration;
        return Task.FromResult(Result<ProductionElevation>.Success(e));
    }

    public Task<Result<ProductionElevation>> RevokeProductionElevationAsync(
        Guid elevationId, string ownerId, RecoveryActor actor, string? reason, CancellationToken cancellationToken = default)
    {
        var e = _elevations.Single(x => x.Id == elevationId && x.OwnerId == ownerId);
        e.RevokedAt = DateTimeOffset.UtcNow;
        e.RevokedByIdentity = actor.Identity;
        return Task.FromResult(Result<ProductionElevation>.Success(e));
    }

    // ── The gate's reads: the in-memory states, over the real ledger for the rest ─────────

    public Task<AutonomyGrant?> GetAutonomyGrantAsync(
        string ownerId, string signatureHash, RecoveryOperationKind actionKind, CancellationToken cancellationToken = default) =>
        Task.FromResult(_grants.GetValueOrDefault((ownerId, signatureHash, actionKind)));

    public Task<ProductionElevation?> GetLiveProductionElevationAsync(
        string ownerId, Guid namespaceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_elevations.FirstOrDefault(e => e.OwnerId == ownerId && e.NamespaceId == namespaceId && e.IsLiveAt(DateTimeOffset.UtcNow)));

    // ── Straight delegation to the real 4.1.0 ledger ────────────────────────────

    public Task<Result<RecoveryOperation>> OpenOperationAsync(OpenRecoveryOperationRequest request, CancellationToken cancellationToken = default) => _real.OpenOperationAsync(request, cancellationToken);
    public Task<Result<RecoveryLedgerEntry>> BeginEntryAsync(BeginRecoveryEntryRequest request, CancellationToken cancellationToken = default) => _real.BeginEntryAsync(request, cancellationToken);
    public Task<Result<RecoveryLedgerEntry>> RecordExecutionAsync(RecordExecutionRequest request, CancellationToken cancellationToken = default) => _real.RecordExecutionAsync(request, cancellationToken);
    public Task<Result<RecoveryLedgerEntry>> RecordObservationAsync(RecordObservationRequest request, CancellationToken cancellationToken = default) => _real.RecordObservationAsync(request, cancellationToken);
    public Task<Result<RecoveryLedgerEntry>> CloseAsync(Guid entryId, string ownerId, RecoveryActor actor, string reason, CancellationToken cancellationToken = default) => _real.CloseAsync(entryId, ownerId, actor, reason, cancellationToken);
    public Task<ChainVerificationResult> VerifyChainAsync(string ownerId, CancellationToken cancellationToken = default) => _real.VerifyChainAsync(ownerId, cancellationToken);
    public Task<RecoveryOperation?> GetOperationAsync(Guid operationId, string ownerId, CancellationToken cancellationToken = default) => _real.GetOperationAsync(operationId, ownerId, cancellationToken);
    public Task<RecoveryLedgerEntry?> GetEntryAsync(Guid entryId, string ownerId, CancellationToken cancellationToken = default) => _real.GetEntryAsync(entryId, ownerId, cancellationToken);
    public Task<IReadOnlyList<RecoveryEvent>> GetEventsForOperationAsync(Guid operationId, string ownerId, CancellationToken cancellationToken = default) => _real.GetEventsForOperationAsync(operationId, ownerId, cancellationToken);
    public Task<IReadOnlyList<RecoveryLedgerEntry>> FindLineageMatchesAsync(string ownerId, Guid? namespaceId, string entityName, string bodyHash, DateTimeOffset since, CancellationToken cancellationToken = default) => _real.FindLineageMatchesAsync(ownerId, namespaceId, entityName, bodyHash, since, cancellationToken);
    public Task<IReadOnlyList<RecoveryLedgerEntry>> GetAgeingAsync(string ownerId, int limit = int.MaxValue, CancellationToken cancellationToken = default) => _real.GetAgeingAsync(ownerId, limit, cancellationToken);
    public Task<RecoveryLedgerEntry?> FindByMarkerAsync(string ownerId, string marker, CancellationToken cancellationToken = default) => _real.FindByMarkerAsync(ownerId, marker, cancellationToken);
    public Task<IReadOnlyList<RecoveryLedgerEntry>> FindHeuristicRecurrenceCandidatesAsync(string ownerId, Guid? namespaceId, string entityName, string bodyHash, DateTimeOffset beganBefore, CancellationToken cancellationToken = default) => _real.FindHeuristicRecurrenceCandidatesAsync(ownerId, namespaceId, entityName, bodyHash, beganBefore, cancellationToken);
    public Task<bool> IsEmergencyStopActiveAsync(string ownerId, CancellationToken cancellationToken = default) => _real.IsEmergencyStopActiveAsync(ownerId, cancellationToken);
}

/// <summary>The archived tests borrow this from 4.0.0's metrics tests, which are not part of this unit.</summary>
internal sealed class FakeMeterFactory : System.Diagnostics.Metrics.IMeterFactory
{
    public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options) => new(options);

    public void Dispose()
    {
    }
}
