using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Helpers;

namespace ServiceHub.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Covers <see cref="NamespaceSignatureHashKindBackfiller"/> (M1.4, ADR-0009 §Decision unit 2) —
/// the classification step that runs once at startup after the migration defaults every existing
/// row to <see cref="SignatureHashKind.Fingerprint"/>.
/// </summary>
public sealed class NamespaceSignatureHashKindBackfillerTests : IDisposable
{
    // Pinned in ADR-0009 and the roadmap: the safety-boundary regression proving fingerprint
    // identity is never touched. Not exercised directly here (this suite doesn't recompute
    // FailureFingerprintBuilder's own algorithm), but every assertion below reuses the same
    // principle — SignatureHash itself must never change.
    private const string PinnedFingerprintHash = "aac7b240aa9569aacc7798817065e2f8a9dd24f176083c50f0ea869a19f64b05";

    private readonly SqliteConnection _connection;
    private readonly DlqDbContext _dbContext;

    public NamespaceSignatureHashKindBackfillerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private async Task<NamespaceSignature> SeedAsync(string hash, string reason, IReadOnlyList<string> topTerms, SignatureHashKind hashKind)
    {
        var signature = new NamespaceSignature
        {
            NamespaceId = Guid.NewGuid(),
            OwnerId = "owner-1",
            SignatureHash = hash,
            HashKind = hashKind,
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            OccurrenceCount = 1,
            DominantDeadletterReason = reason,
            TopTermsJson = System.Text.Json.JsonSerializer.Serialize(topTerms),
        };
        _dbContext.NamespaceSignatures.Add(signature);
        await _dbContext.SaveChangesAsync();
        return signature;
    }

    [Fact]
    public async Task BackfillAsync_RowReproducingClusterHash_IsReclassifiedToCluster()
    {
        var topTerms = new[] { "entity:orders-queue", "reason:MaxDeliveryCountExceeded" };
        var reason = "MaxDeliveryCountExceeded";
        var clusterHash = ClusterSignatureHasher.ComputeHash(topTerms, reason);

        // Simulate the migration's blanket default: the row exists with the cluster hash's own
        // value but was defaulted to Fingerprint by the AddColumn step.
        var seeded = await SeedAsync(clusterHash, reason, topTerms, SignatureHashKind.Fingerprint);

        await NamespaceSignatureHashKindBackfiller.BackfillAsync(_dbContext, NullLogger.Instance);

        var reloaded = await _dbContext.NamespaceSignatures.AsNoTracking().SingleAsync(s => s.Id == seeded.Id);
        reloaded.HashKind.Should().Be(SignatureHashKind.Cluster);
        reloaded.SignatureHash.Should().Be(clusterHash, "the backfill must never change SignatureHash itself");
    }

    [Fact]
    public async Task BackfillAsync_RowNotReproducingClusterHash_StaysFingerprint()
    {
        // A genuine fingerprint-space row: its SignatureHash was computed by
        // FailureFingerprintBuilder over different inputs than TopTermsJson/DominantDeadletterReason
        // reproduce, so ClusterSignatureHasher over the stored terms/reason will never match it.
        var seeded = await SeedAsync(
            PinnedFingerprintHash,
            "MaxDeliveryCountExceeded",
            ["category:MaxDelivery", "deliveries:medium"],
            SignatureHashKind.Fingerprint);

        await NamespaceSignatureHashKindBackfiller.BackfillAsync(_dbContext, NullLogger.Instance);

        var reloaded = await _dbContext.NamespaceSignatures.AsNoTracking().SingleAsync(s => s.Id == seeded.Id);
        reloaded.HashKind.Should().Be(SignatureHashKind.Fingerprint);
        reloaded.SignatureHash.Should().Be(PinnedFingerprintHash);
    }

    [Fact]
    public async Task BackfillAsync_AlreadyClusterRow_IsNeverRescannedOrChanged()
    {
        var topTerms = new[] { "entity:orders-queue" };
        var reason = "MaxDeliveryCountExceeded";
        var clusterHash = ClusterSignatureHasher.ComputeHash(topTerms, reason);
        var seeded = await SeedAsync(clusterHash, reason, topTerms, SignatureHashKind.Cluster);

        await NamespaceSignatureHashKindBackfiller.BackfillAsync(_dbContext, NullLogger.Instance);

        var reloaded = await _dbContext.NamespaceSignatures.AsNoTracking().SingleAsync(s => s.Id == seeded.Id);
        reloaded.HashKind.Should().Be(SignatureHashKind.Cluster);
    }

    [Fact]
    public async Task BackfillAsync_IsIdempotent_SecondRunIsANoOp()
    {
        var topTerms = new[] { "entity:orders-queue" };
        var reason = "MaxDeliveryCountExceeded";
        var clusterHash = ClusterSignatureHasher.ComputeHash(topTerms, reason);
        var seeded = await SeedAsync(clusterHash, reason, topTerms, SignatureHashKind.Fingerprint);

        await NamespaceSignatureHashKindBackfiller.BackfillAsync(_dbContext, NullLogger.Instance);
        await NamespaceSignatureHashKindBackfiller.BackfillAsync(_dbContext, NullLogger.Instance);

        var reloaded = await _dbContext.NamespaceSignatures.AsNoTracking().SingleAsync(s => s.Id == seeded.Id);
        reloaded.HashKind.Should().Be(SignatureHashKind.Cluster);
    }

    [Fact]
    public async Task BackfillAsync_MalformedTopTermsJson_LeftAsFingerprintWithoutThrowing()
    {
        var signature = new NamespaceSignature
        {
            NamespaceId = Guid.NewGuid(),
            OwnerId = "owner-1",
            SignatureHash = "some-hash",
            HashKind = SignatureHashKind.Fingerprint,
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            OccurrenceCount = 1,
            DominantDeadletterReason = "MaxDeliveryCountExceeded",
            TopTermsJson = "not valid json",
        };
        _dbContext.NamespaceSignatures.Add(signature);
        await _dbContext.SaveChangesAsync();

        var act = async () => await NamespaceSignatureHashKindBackfiller.BackfillAsync(_dbContext, NullLogger.Instance);
        await act.Should().NotThrowAsync();

        var reloaded = await _dbContext.NamespaceSignatures.AsNoTracking().SingleAsync(s => s.Id == signature.Id);
        reloaded.HashKind.Should().Be(SignatureHashKind.Fingerprint);
        reloaded.SignatureHash.Should().Be("some-hash");
    }

    [Fact]
    public async Task BackfillAsync_NoCandidates_DoesNothing()
    {
        var act = async () => await NamespaceSignatureHashKindBackfiller.BackfillAsync(_dbContext, NullLogger.Instance);
        await act.Should().NotThrowAsync();
    }
}
