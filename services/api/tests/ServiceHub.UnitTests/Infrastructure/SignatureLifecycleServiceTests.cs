using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure;

public sealed class SignatureLifecycleServiceTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly Mock<IAuditService> _auditServiceMock = new();
    private readonly SignatureLifecycleService _service;
    private static readonly Guid NamespaceId = Guid.NewGuid();
    private const string Hash = "hash-1";

    public SignatureLifecycleServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _service = new SignatureLifecycleService(_dbContext, _auditServiceMock.Object);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetStatusAsync_NeverTransitioned_ReturnsDefaultActive()
    {
        var result = await _service.GetStatusAsync(TestConstants.TestOwnerId, NamespaceId, Hash);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(SignatureLifecycleStatus.Active);
        result.Value.PreviousStatus.Should().BeNull();
        result.Value.TransitionedAt.Should().BeNull();
    }

    [Theory]
    [InlineData(SignatureLifecycleStatus.Active, SignatureLifecycleStatus.Resolved)]
    [InlineData(SignatureLifecycleStatus.Active, SignatureLifecycleStatus.Suppressed)]
    [InlineData(SignatureLifecycleStatus.Active, SignatureLifecycleStatus.Archived)]
    public async Task TransitionAsync_ValidFromActive_Succeeds(SignatureLifecycleStatus from, SignatureLifecycleStatus to)
    {
        from.Should().Be(SignatureLifecycleStatus.Active); // sanity: default starting state

        var result = await _service.TransitionAsync(TestConstants.TestOwnerId, NamespaceId, Hash, to, null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(to);
        result.Value.PreviousStatus.Should().Be(SignatureLifecycleStatus.Active);
    }

    [Fact]
    public async Task TransitionAsync_ResolvedThenReopen_Succeeds()
    {
        await _service.TransitionAsync(TestConstants.TestOwnerId, NamespaceId, Hash, SignatureLifecycleStatus.Resolved, null);

        var result = await _service.TransitionAsync(
            TestConstants.TestOwnerId, NamespaceId, Hash, SignatureLifecycleStatus.Reopened, "recurred");

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(SignatureLifecycleStatus.Reopened);
        result.Value.PreviousStatus.Should().Be(SignatureLifecycleStatus.Resolved);
        result.Value.Notes.Should().Be("recurred");
    }

    [Fact]
    public async Task TransitionAsync_ReopenedThenResolve_Succeeds()
    {
        await _service.TransitionAsync(TestConstants.TestOwnerId, NamespaceId, Hash, SignatureLifecycleStatus.Resolved, null);
        await _service.TransitionAsync(TestConstants.TestOwnerId, NamespaceId, Hash, SignatureLifecycleStatus.Reopened, null);

        var result = await _service.TransitionAsync(
            TestConstants.TestOwnerId, NamespaceId, Hash, SignatureLifecycleStatus.Resolved, null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(SignatureLifecycleStatus.Resolved);
    }

    [Fact]
    public async Task TransitionAsync_ToActive_AlwaysFails()
    {
        var result = await _service.TransitionAsync(
            TestConstants.TestOwnerId, NamespaceId, Hash, SignatureLifecycleStatus.Active, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task TransitionAsync_ReopenFromActive_Fails()
    {
        var result = await _service.TransitionAsync(
            TestConstants.TestOwnerId, NamespaceId, Hash, SignatureLifecycleStatus.Reopened, null);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task TransitionAsync_AnyTransitionOutOfArchived_Fails()
    {
        await _service.TransitionAsync(TestConstants.TestOwnerId, NamespaceId, Hash, SignatureLifecycleStatus.Archived, null);

        var toResolved = await _service.TransitionAsync(
            TestConstants.TestOwnerId, NamespaceId, Hash, SignatureLifecycleStatus.Resolved, null);
        var toReopened = await _service.TransitionAsync(
            TestConstants.TestOwnerId, NamespaceId, Hash, SignatureLifecycleStatus.Reopened, null);
        var toSuppressed = await _service.TransitionAsync(
            TestConstants.TestOwnerId, NamespaceId, Hash, SignatureLifecycleStatus.Suppressed, null);

        toResolved.IsFailure.Should().BeTrue();
        toReopened.IsFailure.Should().BeTrue();
        toSuppressed.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task GetHistoryAsync_AccumulatesEventsInOrder()
    {
        await _service.TransitionAsync(TestConstants.TestOwnerId, NamespaceId, Hash, SignatureLifecycleStatus.Resolved, "r1");
        await _service.TransitionAsync(TestConstants.TestOwnerId, NamespaceId, Hash, SignatureLifecycleStatus.Reopened, "r2");
        await _service.TransitionAsync(TestConstants.TestOwnerId, NamespaceId, Hash, SignatureLifecycleStatus.Archived, "r3");

        var history = await _service.GetHistoryAsync(TestConstants.TestOwnerId, NamespaceId, Hash);

        history.IsSuccess.Should().BeTrue();
        history.Value.Should().HaveCount(3);
        history.Value[0].ToStatus.Should().Be(SignatureLifecycleStatus.Resolved);
        history.Value[1].ToStatus.Should().Be(SignatureLifecycleStatus.Reopened);
        history.Value[2].ToStatus.Should().Be(SignatureLifecycleStatus.Archived);
    }

    [Fact]
    public async Task GetHistoryAsync_NeverTransitioned_ReturnsEmpty()
    {
        var history = await _service.GetHistoryAsync(TestConstants.TestOwnerId, NamespaceId, "untouched-hash");

        history.IsSuccess.Should().BeTrue();
        history.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Keys_AreIsolatedByOwnerNamespaceAndHash()
    {
        await _service.TransitionAsync(TestConstants.TestOwnerId, NamespaceId, Hash, SignatureLifecycleStatus.Resolved, null);

        var otherOwner = await _service.GetStatusAsync(TestConstants.AltOwnerId, NamespaceId, Hash);
        var otherNamespace = await _service.GetStatusAsync(TestConstants.TestOwnerId, Guid.NewGuid(), Hash);
        var otherHash = await _service.GetStatusAsync(TestConstants.TestOwnerId, NamespaceId, "different-hash");

        otherOwner.Value.Status.Should().Be(SignatureLifecycleStatus.Active);
        otherNamespace.Value.Status.Should().Be(SignatureLifecycleStatus.Active);
        otherHash.Value.Status.Should().Be(SignatureLifecycleStatus.Active);
    }

    [Fact]
    public async Task GetStatusBatchAsync_MixOfKnownAndUnknownHashes_ReturnsCorrectStatusForEach()
    {
        await _service.TransitionAsync(TestConstants.TestOwnerId, NamespaceId, "hash-a", SignatureLifecycleStatus.Resolved, null);

        var result = await _service.GetStatusBatchAsync(
            TestConstants.TestOwnerId, NamespaceId, ["hash-a", "hash-b"]);

        result.IsSuccess.Should().BeTrue();
        result.Value["hash-a"].Status.Should().Be(SignatureLifecycleStatus.Resolved);
        result.Value["hash-b"].Status.Should().Be(SignatureLifecycleStatus.Active);
    }

    [Fact]
    public async Task TransitionAsync_ConcurrentTransitionsOnDistinctHashes_DoNotInterfere()
    {
        var hashes = Enumerable.Range(0, 20).Select(i => $"hash-{i}").ToList();

        await Task.WhenAll(hashes.Select(h =>
            _service.TransitionAsync(TestConstants.TestOwnerId, NamespaceId, h, SignatureLifecycleStatus.Resolved, null)));

        var batch = await _service.GetStatusBatchAsync(TestConstants.TestOwnerId, NamespaceId, hashes);

        batch.Value.Should().HaveCount(20);
        batch.Value.Values.Should().OnlyContain(s => s.Status == SignatureLifecycleStatus.Resolved);
    }

    [Fact]
    public async Task TransitionAsync_Succeeds_PersistsAcrossNewDbContextInstance_SimulatingRestart()
    {
        await _service.TransitionAsync(TestConstants.TestOwnerId, NamespaceId, Hash, SignatureLifecycleStatus.Resolved, "note");

        // A fresh DbContext over the same underlying connection stands in for "a new process
        // reading the same on-disk database after a restart" — the point being proven is that
        // state lives in the store, not in this SignatureLifecycleService instance's memory.
        var freshOptions = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite(_dbContext.Database.GetDbConnection())
            .Options;
        using var freshDbContext = new DlqDbContext(freshOptions);
        var freshService = new SignatureLifecycleService(freshDbContext, _auditServiceMock.Object);

        var result = await freshService.GetStatusAsync(TestConstants.TestOwnerId, NamespaceId, Hash);

        result.Value.Status.Should().Be(SignatureLifecycleStatus.Resolved);
        result.Value.Notes.Should().Be("note");
    }

    [Fact]
    public async Task TransitionAsync_Succeeds_EnqueuesAuditLogEntry()
    {
        await _service.TransitionAsync(TestConstants.TestOwnerId, NamespaceId, Hash, SignatureLifecycleStatus.Resolved, "note");

        _auditServiceMock.Verify(a => a.Enqueue(It.Is<AuditLog>(log =>
            log.OwnerId == TestConstants.TestOwnerId &&
            log.NamespaceId == NamespaceId &&
            log.ResourceName == Hash &&
            log.Outcome == nameof(SignatureLifecycleStatus.Resolved))), Times.Once);
    }

    [Fact]
    public async Task TransitionAsync_InvalidTransition_DoesNotEnqueueAuditLogEntry()
    {
        await _service.TransitionAsync(TestConstants.TestOwnerId, NamespaceId, Hash, SignatureLifecycleStatus.Reopened, null);

        _auditServiceMock.Verify(a => a.Enqueue(It.IsAny<AuditLog>()), Times.Never);
    }
}
