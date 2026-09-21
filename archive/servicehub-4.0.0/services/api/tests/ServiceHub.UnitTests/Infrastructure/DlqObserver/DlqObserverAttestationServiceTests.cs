using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Infrastructure.DlqObserver;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure.DlqObserver;

public sealed class DlqObserverAttestationServiceTests : IDisposable
{
    private const string OwnerId = "owner-a";

    private readonly DlqDbContext _dbContext;
    private readonly DlqObserverAttestationService _service;

    public DlqObserverAttestationServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _service = new DlqObserverAttestationService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    [Fact]
    public async Task IsLiveAsync_NoRowAtAll_FailsClosedToFalse()
    {
        var isLive = await _service.IsLiveAsync(OwnerId, Guid.NewGuid());

        isLive.Should().BeFalse();
    }

    [Fact]
    public async Task ConfigureAsync_EnabledWithoutDlqEntityName_Fails()
    {
        var result = await _service.ConfigureAsync(
            OwnerId, Guid.NewGuid(), enabled: true, observerReference: "table", dlqEntityName: null, stalenessBoundMinutes: 60);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ConfigureAsync_NonPositiveStalenessBound_Fails()
    {
        var result = await _service.ConfigureAsync(
            OwnerId, Guid.NewGuid(), enabled: false, observerReference: null, dlqEntityName: null, stalenessBoundMinutes: 0);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ConfigureAsync_Valid_CreatesRow_NotYetLive()
    {
        var namespaceId = Guid.NewGuid();
        var result = await _service.ConfigureAsync(
            OwnerId, namespaceId, enabled: true, observerReference: "dlq-observations", dlqEntityName: "orders-dlq",
            stalenessBoundMinutes: 60);

        result.IsSuccess.Should().BeTrue();
        result.Value.Enabled.Should().BeTrue();

        // Enabled but never confirmed — never "assume fine" (ADR-004 item 4).
        (await _service.IsLiveAsync(OwnerId, namespaceId)).Should().BeFalse();
    }

    [Fact]
    public async Task RecordCanarySentThenConfirmed_BecomesLive()
    {
        var namespaceId = Guid.NewGuid();
        await _service.ConfigureAsync(
            OwnerId, namespaceId, enabled: true, observerReference: "dlq-observations", dlqEntityName: "orders-dlq",
            stalenessBoundMinutes: 60);

        await _service.RecordCanarySentAsync(OwnerId, namespaceId, "canary-1");
        (await _service.IsLiveAsync(OwnerId, namespaceId)).Should().BeFalse("sent, not yet confirmed");

        await _service.RecordCanaryConfirmedAsync(OwnerId, namespaceId);
        (await _service.IsLiveAsync(OwnerId, namespaceId)).Should().BeTrue();
    }

    [Fact]
    public async Task DisablingAfterConfirmation_IsNoLongerLive()
    {
        var namespaceId = Guid.NewGuid();
        await _service.ConfigureAsync(
            OwnerId, namespaceId, enabled: true, observerReference: "dlq-observations", dlqEntityName: "orders-dlq",
            stalenessBoundMinutes: 60);
        await _service.RecordCanaryConfirmedAsync(OwnerId, namespaceId);
        (await _service.IsLiveAsync(OwnerId, namespaceId)).Should().BeTrue();

        await _service.ConfigureAsync(
            OwnerId, namespaceId, enabled: false, observerReference: "dlq-observations", dlqEntityName: "orders-dlq",
            stalenessBoundMinutes: 60);

        (await _service.IsLiveAsync(OwnerId, namespaceId)).Should().BeFalse();
    }

    [Fact]
    public async Task GetAllEnabledAsync_OnlyReturnsEnabledRows_AcrossOwners()
    {
        await _service.ConfigureAsync(
            OwnerId, Guid.NewGuid(), enabled: true, observerReference: "t1", dlqEntityName: "dlq1", stalenessBoundMinutes: 60);
        await _service.ConfigureAsync(
            "owner-b", Guid.NewGuid(), enabled: true, observerReference: "t2", dlqEntityName: "dlq2", stalenessBoundMinutes: 60);
        await _service.ConfigureAsync(
            OwnerId, Guid.NewGuid(), enabled: false, observerReference: null, dlqEntityName: null, stalenessBoundMinutes: 60);

        var enabled = await _service.GetAllEnabledAsync();

        enabled.Should().HaveCount(2);
    }
}
