using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure;

public sealed class FailureIntelligenceCenterServiceTests : IDisposable
{
    private const string OwnerId = "entra:test-owner-123";

    private readonly DlqDbContext _dbContext;
    private readonly Mock<INamespaceSignatureLookupService> _signatureLookupMock = new();
    private readonly Mock<ISignatureLifecycleService> _lifecycleMock = new();
    private readonly Mock<IFailureKnowledgeService> _knowledgeMock = new();
    private readonly FailureIntelligenceCenterService _sut;

    public FailureIntelligenceCenterServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _lifecycleMock
            .Setup(l => l.GetStatusAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SignatureLifecycleSnapshot>.Success(
                new SignatureLifecycleSnapshot(SignatureLifecycleStatus.Active, null, null, null)));

        _sut = new FailureIntelligenceCenterService(
            _dbContext, _signatureLookupMock.Object, _lifecycleMock.Object, _knowledgeMock.Object);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static NamespaceSignature MakeSignature(Guid namespaceId, string hash) => new()
    {
        NamespaceId = namespaceId,
        OwnerId = OwnerId,
        SignatureHash = hash,
        FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-10),
        LastSeenAt = DateTimeOffset.UtcNow.AddDays(-1),
        OccurrenceCount = 4,
        DominantDeadletterReason = "MaxDeliveryCountExceeded",
        TopTermsJson = "[\"timeout\"]",
    };

    private static SignatureReplayJob MakeJob(
        Guid namespaceId, string hash, BulkOperationStatus status, DateTimeOffset createdAt,
        int totalMatched = 5, int failureCount = 0) => new()
    {
        OwnerId = OwnerId,
        NamespaceId = namespaceId,
        NamespaceDisplayName = "test-namespace",
        SignatureHash = hash,
        MessageIdsJson = "[]",
        Status = status,
        CreatedAt = createdAt,
        CompletedAt = createdAt.AddMinutes(1),
        TotalMatched = totalMatched,
        ProcessedCount = totalMatched,
        FailureCount = failureCount,
        SuccessCount = totalMatched - failureCount,
    };

    [Fact]
    public async Task GetInvestigationCenterAsync_NoReplayJobs_ReturnsEmptyFailedReplays()
    {
        var namespaceId = Guid.NewGuid();
        _dbContext.NamespaceSignatures.Add(MakeSignature(namespaceId, "hash-no-jobs"));
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetInvestigationCenterAsync(OwnerId);

        result.IsSuccess.Should().BeTrue();
        result.Value.FailedReplays.Should().BeEmpty();
    }

    [Fact]
    public async Task GetInvestigationCenterAsync_MostRecentJobFailed_IncludesFailedReplayWithRecommendation()
    {
        var namespaceId = Guid.NewGuid();
        const string hash = "hash-failed";
        _dbContext.NamespaceSignatures.Add(MakeSignature(namespaceId, hash));
        _dbContext.SignatureReplayJobs.Add(
            MakeJob(namespaceId, hash, BulkOperationStatus.Failed, DateTimeOffset.UtcNow.AddDays(-1), failureCount: 5));
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetInvestigationCenterAsync(OwnerId);

        result.IsSuccess.Should().BeTrue();
        result.Value.FailedReplays.Should().ContainSingle();
        var item = result.Value.FailedReplays[0];
        item.NamespaceId.Should().Be(namespaceId);
        item.SignatureHash.Should().Be(hash);
        item.JobStatus.Should().Be(nameof(BulkOperationStatus.Failed));
        item.RecommendedNextAction.Should().Be("Investigate the underlying failure before replaying again.");
    }

    [Fact]
    public async Task GetInvestigationCenterAsync_MostRecentJobCompletedWithErrors_UsesReviewRecommendation()
    {
        var namespaceId = Guid.NewGuid();
        const string hash = "hash-errors";
        _dbContext.NamespaceSignatures.Add(MakeSignature(namespaceId, hash));
        _dbContext.SignatureReplayJobs.Add(
            MakeJob(namespaceId, hash, BulkOperationStatus.CompletedWithErrors, DateTimeOffset.UtcNow.AddHours(-3), failureCount: 2));
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetInvestigationCenterAsync(OwnerId);

        result.Value.FailedReplays.Should().ContainSingle();
        result.Value.FailedReplays[0].RecommendedNextAction.Should().Be("Review the failure sample before retrying.");
        result.Value.FailedReplays[0].FailedCount.Should().Be(2);
    }

    [Fact]
    public async Task GetInvestigationCenterAsync_LatestJobSucceededAfterEarlierFailure_ExcludesSignature()
    {
        var namespaceId = Guid.NewGuid();
        const string hash = "hash-recovered";
        _dbContext.NamespaceSignatures.Add(MakeSignature(namespaceId, hash));
        _dbContext.SignatureReplayJobs.AddRange(
            MakeJob(namespaceId, hash, BulkOperationStatus.Failed, DateTimeOffset.UtcNow.AddDays(-2)),
            MakeJob(namespaceId, hash, BulkOperationStatus.Completed, DateTimeOffset.UtcNow.AddDays(-1)));
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetInvestigationCenterAsync(OwnerId);

        result.Value.FailedReplays.Should().BeEmpty();
    }

    [Fact]
    public async Task GetInvestigationCenterAsync_FailedJobOutsideSevenDayWindow_IsExcluded()
    {
        var namespaceId = Guid.NewGuid();
        const string hash = "hash-stale";
        _dbContext.NamespaceSignatures.Add(MakeSignature(namespaceId, hash));
        _dbContext.SignatureReplayJobs.Add(
            MakeJob(namespaceId, hash, BulkOperationStatus.Failed, DateTimeOffset.UtcNow.AddDays(-10)));
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetInvestigationCenterAsync(OwnerId);

        result.Value.FailedReplays.Should().BeEmpty();
    }

    [Fact]
    public async Task GetInvestigationCenterAsync_MultipleFailedSignatures_OrdersByMostRecentFirst()
    {
        var namespaceId = Guid.NewGuid();
        const string olderHash = "hash-older";
        const string newerHash = "hash-newer";
        _dbContext.NamespaceSignatures.Add(MakeSignature(namespaceId, olderHash));
        _dbContext.NamespaceSignatures.Add(MakeSignature(namespaceId, newerHash));
        _dbContext.SignatureReplayJobs.AddRange(
            MakeJob(namespaceId, olderHash, BulkOperationStatus.Failed, DateTimeOffset.UtcNow.AddDays(-3)),
            MakeJob(namespaceId, newerHash, BulkOperationStatus.Failed, DateTimeOffset.UtcNow.AddHours(-1)));
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetInvestigationCenterAsync(OwnerId);

        result.Value.FailedReplays.Should().HaveCount(2);
        result.Value.FailedReplays[0].SignatureHash.Should().Be(newerHash);
        result.Value.FailedReplays[1].SignatureHash.Should().Be(olderHash);
    }
}
