using FluentAssertions;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ServiceHub.UnitTests;

public sealed class FailureKnowledgeServiceTests
{
    private readonly DlqDbContext _dbContext;
    private readonly FailureKnowledgeService _sut;
    private readonly ILogger<FailureKnowledgeService> _logger;

    private const string OwnerId = "entra:test-owner";
    private static readonly Guid NamespaceId = Guid.NewGuid();
    private const string SignatureHash = "hash-test-123";

    public FailureKnowledgeServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseInMemoryDatabase($"test-{Guid.NewGuid()}")
            .Options;

        _dbContext = new DlqDbContext(options);
        _logger = Moq.Mock.Of<ILogger<FailureKnowledgeService>>();
        _sut = new FailureKnowledgeService(_dbContext, _logger);
    }

    [Fact]
    public async Task GetKnowledgeAsync_NoExistingKnowledge_ReturnsDefaultEmpty()
    {
        var result = await _sut.GetKnowledgeAsync(OwnerId, NamespaceId, SignatureHash);

        result.IsSuccess.Should().BeTrue();
        result.Value.RootCause.Should().BeNull();
        result.Value.ResolutionNotes.Should().BeNull();
        result.Value.Owner.Should().BeNull();
        result.Value.KnowledgeVersion.Should().Be(1);
    }

    [Fact]
    public async Task UpsertKnowledgeAsync_NewKnowledge_CreateAndReturns()
    {
        var knowledge = new FailureKnowledge(
            RootCause: "Database connection timeout",
            ResolutionNotes: "Restart connection pool",
            OperationalNotes: "Happens during peak hours",
            RunbookLink: "https://wiki/runbooks/db-timeout",
            Owner: "platform-team@example.com",
            ReplayGuidance: "safe",
            LastUpdatedAt: null,
            KnowledgeVersion: 1,
            ReviewDueAt: null,
            Tags: "transient;database");

        var result = await _sut.UpsertKnowledgeAsync(OwnerId, NamespaceId, SignatureHash, knowledge);

        result.IsSuccess.Should().BeTrue();
        result.Value.RootCause.Should().Be("Database connection timeout");
        result.Value.Owner.Should().Be("platform-team@example.com");
        result.Value.KnowledgeVersion.Should().Be(1);
    }

    [Fact]
    public async Task UpsertKnowledgeAsync_UpdateExisting_IncrementsVersion()
    {
        var original = new FailureKnowledge(
            RootCause: "Original cause",
            ResolutionNotes: null,
            OperationalNotes: null,
            RunbookLink: null,
            Owner: null,
            ReplayGuidance: null,
            LastUpdatedAt: null,
            KnowledgeVersion: 1,
            ReviewDueAt: null,
            Tags: null);

        await _sut.UpsertKnowledgeAsync(OwnerId, NamespaceId, SignatureHash, original);

        var updated = new FailureKnowledge(
            RootCause: "Updated cause",
            ResolutionNotes: "New resolution",
            OperationalNotes: null,
            RunbookLink: null,
            Owner: "new-owner@example.com",
            ReplayGuidance: "unsafe",
            LastUpdatedAt: null,
            KnowledgeVersion: 1,
            ReviewDueAt: null,
            Tags: null);

        var result = await _sut.UpsertKnowledgeAsync(OwnerId, NamespaceId, SignatureHash, updated);

        result.IsSuccess.Should().BeTrue();
        result.Value.RootCause.Should().Be("Updated cause");
        result.Value.Owner.Should().Be("new-owner@example.com");
        result.Value.KnowledgeVersion.Should().Be(2);
    }

    [Fact]
    public async Task GetKnowledgeBatchAsync_MultipleHashes_ReturnsAll()
    {
        var hash1 = "hash-1";
        var hash2 = "hash-2";
        var hash3 = "hash-3";

        // Create knowledge for hash1 and hash2
        var knowledge1 = new FailureKnowledge(
            RootCause: "Cause 1", ResolutionNotes: null, OperationalNotes: null,
            RunbookLink: null, Owner: null, ReplayGuidance: null, LastUpdatedAt: null,
            KnowledgeVersion: 1, ReviewDueAt: null, Tags: null);

        var knowledge2 = new FailureKnowledge(
            RootCause: "Cause 2", ResolutionNotes: null, OperationalNotes: null,
            RunbookLink: null, Owner: null, ReplayGuidance: null, LastUpdatedAt: null,
            KnowledgeVersion: 1, ReviewDueAt: null, Tags: null);

        await _sut.UpsertKnowledgeAsync(OwnerId, NamespaceId, hash1, knowledge1);
        await _sut.UpsertKnowledgeAsync(OwnerId, NamespaceId, hash2, knowledge2);

        // Query for all three
        var result = await _sut.GetKnowledgeBatchAsync(OwnerId, NamespaceId, [hash1, hash2, hash3]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value[hash1].RootCause.Should().Be("Cause 1");
        result.Value[hash2].RootCause.Should().Be("Cause 2");
        result.Value[hash3].RootCause.Should().BeNull(); // Default empty
    }

    [Fact]
    public async Task MarkForReviewAsync_SetReviewDate()
    {
        var reviewDate = DateTimeOffset.UtcNow.AddDays(7);

        var result = await _sut.MarkForReviewAsync(OwnerId, NamespaceId, SignatureHash, reviewDate);

        result.IsSuccess.Should().BeTrue();
        result.Value.ReviewDueAt.Should().Be(reviewDate);
    }

    [Fact]
    public async Task UpsertKnowledgeAsync_NewKnowledge_NoHistorySnapshotTaken()
    {
        var knowledge = new FailureKnowledge(
            RootCause: "Cause", ResolutionNotes: null, OperationalNotes: null,
            RunbookLink: null, Owner: null, ReplayGuidance: null, LastUpdatedAt: null,
            KnowledgeVersion: 1, ReviewDueAt: null, Tags: null);

        await _sut.UpsertKnowledgeAsync(OwnerId, NamespaceId, SignatureHash, knowledge);

        var history = await _sut.GetKnowledgeHistoryAsync(OwnerId, NamespaceId, SignatureHash);

        history.IsSuccess.Should().BeTrue();
        history.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task UpsertKnowledgeAsync_UpdateExisting_SnapshotsPriorVersionIntoHistory()
    {
        var original = new FailureKnowledge(
            RootCause: "Original cause", ResolutionNotes: null, OperationalNotes: null,
            RunbookLink: null, Owner: null, ReplayGuidance: null, LastUpdatedAt: null,
            KnowledgeVersion: 1, ReviewDueAt: null, Tags: null, UpdatedBy: "alice@example.com");

        await _sut.UpsertKnowledgeAsync(OwnerId, NamespaceId, SignatureHash, original);

        var updated = new FailureKnowledge(
            RootCause: "Updated cause", ResolutionNotes: null, OperationalNotes: null,
            RunbookLink: null, Owner: null, ReplayGuidance: null, LastUpdatedAt: null,
            KnowledgeVersion: 1, ReviewDueAt: null, Tags: null, UpdatedBy: "bob@example.com");

        await _sut.UpsertKnowledgeAsync(OwnerId, NamespaceId, SignatureHash, updated);

        var history = await _sut.GetKnowledgeHistoryAsync(OwnerId, NamespaceId, SignatureHash);

        history.IsSuccess.Should().BeTrue();
        history.Value.Should().HaveCount(1);
        history.Value[0].KnowledgeVersion.Should().Be(1);
        history.Value[0].RootCause.Should().Be("Original cause");
        history.Value[0].UpdatedBy.Should().Be("alice@example.com");
    }

    [Fact]
    public async Task GetKnowledgeHistoryAsync_MultipleUpdates_OrdersMostRecentFirst()
    {
        var v1 = new FailureKnowledge(
            RootCause: "v1", ResolutionNotes: null, OperationalNotes: null,
            RunbookLink: null, Owner: null, ReplayGuidance: null, LastUpdatedAt: null,
            KnowledgeVersion: 1, ReviewDueAt: null, Tags: null);
        await _sut.UpsertKnowledgeAsync(OwnerId, NamespaceId, SignatureHash, v1);

        var v2 = v1 with { RootCause = "v2" };
        await _sut.UpsertKnowledgeAsync(OwnerId, NamespaceId, SignatureHash, v2);

        var v3 = v1 with { RootCause = "v3" };
        await _sut.UpsertKnowledgeAsync(OwnerId, NamespaceId, SignatureHash, v3);

        var history = await _sut.GetKnowledgeHistoryAsync(OwnerId, NamespaceId, SignatureHash);

        history.IsSuccess.Should().BeTrue();
        history.Value.Should().HaveCount(2);
        history.Value[0].KnowledgeVersion.Should().Be(2);
        history.Value[0].RootCause.Should().Be("v2");
        history.Value[1].KnowledgeVersion.Should().Be(1);
        history.Value[1].RootCause.Should().Be("v1");
    }

    [Fact]
    public async Task UpsertKnowledgeAsync_UpdatedByOmitted_PersistsNull()
    {
        var knowledge = new FailureKnowledge(
            RootCause: "Cause", ResolutionNotes: null, OperationalNotes: null,
            RunbookLink: null, Owner: null, ReplayGuidance: null, LastUpdatedAt: null,
            KnowledgeVersion: 1, ReviewDueAt: null, Tags: null);

        var result = await _sut.UpsertKnowledgeAsync(OwnerId, NamespaceId, SignatureHash, knowledge);

        result.Value.UpdatedBy.Should().BeNull();
    }

    [Fact]
    public async Task FailureKnowledgeService_OwnerScoping_IsolatesData()
    {
        var owner1 = "owner1";
        var owner2 = "owner2";

        var knowledge1 = new FailureKnowledge(
            RootCause: "Owner1 cause", ResolutionNotes: null, OperationalNotes: null,
            RunbookLink: null, Owner: null, ReplayGuidance: null, LastUpdatedAt: null,
            KnowledgeVersion: 1, ReviewDueAt: null, Tags: null);

        var knowledge2 = new FailureKnowledge(
            RootCause: "Owner2 cause", ResolutionNotes: null, OperationalNotes: null,
            RunbookLink: null, Owner: null, ReplayGuidance: null, LastUpdatedAt: null,
            KnowledgeVersion: 1, ReviewDueAt: null, Tags: null);

        await _sut.UpsertKnowledgeAsync(owner1, NamespaceId, SignatureHash, knowledge1);
        await _sut.UpsertKnowledgeAsync(owner2, NamespaceId, SignatureHash, knowledge2);

        // Verify isolation
        var result1 = await _sut.GetKnowledgeAsync(owner1, NamespaceId, SignatureHash);
        var result2 = await _sut.GetKnowledgeAsync(owner2, NamespaceId, SignatureHash);

        result1.Value.RootCause.Should().Be("Owner1 cause");
        result2.Value.RootCause.Should().Be("Owner2 cause");
    }
}
