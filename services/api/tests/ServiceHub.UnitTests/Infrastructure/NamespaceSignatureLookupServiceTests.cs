using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure;

public sealed class NamespaceSignatureLookupServiceTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly NamespaceSignatureLookupService _service;

    public NamespaceSignatureLookupServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _service = new NamespaceSignatureLookupService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static ClusterSignatureObservation MakeObservation(string hash = "hash-1") =>
        new(SignatureHash: hash, DominantDeadletterReason: "MaxDeliveryCountExceeded", TopTerms: new[] { "timeout", "sql" });

    [Fact]
    public async Task LookupAndRecordAsync_FirstObservation_IsNewTrueWithCountOne()
    {
        var namespaceId = Guid.NewGuid();
        var observation = MakeObservation();

        var results = await _service.LookupAndRecordAsync(
            TestConstants.TestOwnerId, namespaceId, new[] { observation });

        results.Should().ContainKey(observation.SignatureHash);
        var result = results[observation.SignatureHash];
        result.IsNew.Should().BeTrue();
        result.OccurrenceCount.Should().Be(1);
    }

    [Fact]
    public async Task LookupAndRecordAsync_SecondObservation_IsNewFalseWithCountTwo()
    {
        var namespaceId = Guid.NewGuid();
        var observation = MakeObservation();

        await _service.LookupAndRecordAsync(TestConstants.TestOwnerId, namespaceId, new[] { observation });
        var results = await _service.LookupAndRecordAsync(TestConstants.TestOwnerId, namespaceId, new[] { observation });

        var result = results[observation.SignatureHash];
        result.IsNew.Should().BeFalse();
        result.OccurrenceCount.Should().Be(2);
    }

    [Fact]
    public async Task LookupAndRecordAsync_SecondObservation_FirstSeenAtUnchanged()
    {
        var namespaceId = Guid.NewGuid();
        var observation = MakeObservation();

        var firstResults = await _service.LookupAndRecordAsync(TestConstants.TestOwnerId, namespaceId, new[] { observation });
        var firstSeenAt = firstResults[observation.SignatureHash].FirstSeenAt;

        var secondResults = await _service.LookupAndRecordAsync(TestConstants.TestOwnerId, namespaceId, new[] { observation });

        secondResults[observation.SignatureHash].FirstSeenAt.Should().Be(firstSeenAt);
    }

    [Fact]
    public async Task LookupAndRecordAsync_OwnerIsolation_OwnerAHistoryDoesNotAffectOwnerB()
    {
        var namespaceId = Guid.NewGuid();
        var observation = MakeObservation();

        // Owner A observes this signature twice.
        await _service.LookupAndRecordAsync(TestConstants.TestOwnerId, namespaceId, new[] { observation });
        await _service.LookupAndRecordAsync(TestConstants.TestOwnerId, namespaceId, new[] { observation });

        // Owner B observes the same hash in the same namespace for the first time.
        var ownerBResults = await _service.LookupAndRecordAsync(TestConstants.AltOwnerId, namespaceId, new[] { observation });

        var ownerBResult = ownerBResults[observation.SignatureHash];
        ownerBResult.IsNew.Should().BeTrue();
        ownerBResult.OccurrenceCount.Should().Be(1);
    }

    [Fact]
    public async Task LookupAndRecordAsync_DuplicateHashesInSameBatch_IsIdempotent()
    {
        var namespaceId = Guid.NewGuid();
        var observation = MakeObservation();

        var results = await _service.LookupAndRecordAsync(
            TestConstants.TestOwnerId, namespaceId, new[] { observation, observation, observation });

        results[observation.SignatureHash].OccurrenceCount.Should().Be(1);

        var storedCount = await _dbContext.NamespaceSignatures.CountAsync(
            s => s.OwnerId == TestConstants.TestOwnerId && s.NamespaceId == namespaceId
                 && s.SignatureHash == observation.SignatureHash);
        storedCount.Should().Be(1);
    }

    [Fact]
    public async Task LookupAndRecordAsync_DifferentNamespace_TreatedAsNew()
    {
        var observation = MakeObservation();

        await _service.LookupAndRecordAsync(TestConstants.TestOwnerId, Guid.NewGuid(), new[] { observation });
        var resultsInOtherNamespace = await _service.LookupAndRecordAsync(
            TestConstants.TestOwnerId, Guid.NewGuid(), new[] { observation });

        resultsInOtherNamespace[observation.SignatureHash].IsNew.Should().BeTrue();
    }

    [Fact]
    public async Task LookupAndRecordAsync_PersistsExplanationSnapshot()
    {
        var namespaceId = Guid.NewGuid();
        var observation = MakeObservation();

        await _service.LookupAndRecordAsync(TestConstants.TestOwnerId, namespaceId, new[] { observation });

        var stored = await _dbContext.NamespaceSignatures.SingleAsync(
            s => s.OwnerId == TestConstants.TestOwnerId && s.NamespaceId == namespaceId
                 && s.SignatureHash == observation.SignatureHash);

        stored.DominantDeadletterReason.Should().Be(observation.DominantDeadletterReason);
        stored.TopTermsJson.Should().Contain("timeout").And.Contain("sql");
    }

    [Fact]
    public void Constructor_NullDbContext_Throws()
    {
        var act = () => new NamespaceSignatureLookupService(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dbContext");
    }

    // ── GetByHashAsync ──────────────────────────────────────

    [Fact]
    public async Task GetByHashAsync_KnownHash_ReturnsRecord()
    {
        var namespaceId = Guid.NewGuid();
        var observation = MakeObservation();
        await _service.LookupAndRecordAsync(TestConstants.TestOwnerId, namespaceId, new[] { observation });

        var result = await _service.GetByHashAsync(TestConstants.TestOwnerId, namespaceId, observation.SignatureHash);

        result.Should().NotBeNull();
        result!.SignatureHash.Should().Be(observation.SignatureHash);
    }

    [Fact]
    public async Task GetByHashAsync_UnknownHash_ReturnsNull()
    {
        var result = await _service.GetByHashAsync(TestConstants.TestOwnerId, Guid.NewGuid(), "never-seen");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByHashAsync_DoesNotMutateOccurrenceCountOrLastSeenAt()
    {
        var namespaceId = Guid.NewGuid();
        var observation = MakeObservation();
        await _service.LookupAndRecordAsync(TestConstants.TestOwnerId, namespaceId, new[] { observation });

        var before = await _service.GetByHashAsync(TestConstants.TestOwnerId, namespaceId, observation.SignatureHash);
        await _service.GetByHashAsync(TestConstants.TestOwnerId, namespaceId, observation.SignatureHash);
        await _service.GetByHashAsync(TestConstants.TestOwnerId, namespaceId, observation.SignatureHash);
        var after = await _service.GetByHashAsync(TestConstants.TestOwnerId, namespaceId, observation.SignatureHash);

        after!.OccurrenceCount.Should().Be(before!.OccurrenceCount);
        after.LastSeenAt.Should().Be(before.LastSeenAt);
    }
}
