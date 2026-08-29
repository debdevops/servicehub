using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure;

public sealed class ExternalSignalRepositoryTests : IDisposable
{
    private const string OwnerA = "entra:owner-a";
    private const string OwnerB = "entra:owner-b";

    private readonly DlqDbContext _dbContext;
    private readonly ExternalSignalRepository _sut;

    public ExternalSignalRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _sut = new ExternalSignalRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static RecordExternalSignalRequest BuildRequest(
        string ownerId = OwnerA,
        Guid? namespaceId = null,
        DateTimeOffset? occurredAt = null,
        string source = "manual",
        string? detailJson = null) => new()
    {
        OwnerId = ownerId,
        NamespaceId = namespaceId,
        SignalType = ExternalSignalType.Deploy,
        OccurredAt = occurredAt ?? DateTimeOffset.UtcNow,
        Source = source,
        DetailJson = detailJson,
    };

    [Fact]
    public async Task RecordAsync_ValidRequest_PersistsSignal()
    {
        var result = await _sut.RecordAsync(BuildRequest());

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().NotBeEmpty();
        result.Value.IngestedAt.Should().NotBe(default);

        var persisted = await _dbContext.ExternalSignalEvents.FirstOrDefaultAsync(s => s.Id == result.Value.Id);
        persisted.Should().NotBeNull();
    }

    [Fact]
    public async Task RecordAsync_BlankSource_ReturnsValidationFailure()
    {
        var result = await _sut.RecordAsync(BuildRequest(source: " "));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ExternalSignal.SourceRequired");
    }

    [Fact]
    public async Task QueryAsync_FiltersByOwner()
    {
        await _sut.RecordAsync(BuildRequest(OwnerA));
        await _sut.RecordAsync(BuildRequest(OwnerB));

        var results = await _sut.QueryAsync(OwnerA, null, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), 100);

        results.Should().ContainSingle();
        results[0].OwnerId.Should().Be(OwnerA);
    }

    [Fact]
    public async Task QueryAsync_NamespaceFilter_ExcludesFleetWideSignals()
    {
        var namespaceId = Guid.NewGuid();
        await _sut.RecordAsync(BuildRequest(OwnerA, namespaceId: namespaceId));
        await _sut.RecordAsync(BuildRequest(OwnerA, namespaceId: null));

        var results = await _sut.QueryAsync(OwnerA, namespaceId, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), 100);

        results.Should().ContainSingle();
        results[0].NamespaceId.Should().Be(namespaceId);
    }

    [Fact]
    public async Task QueryAsync_OutsideTimeWindow_ExcludesSignal()
    {
        await _sut.RecordAsync(BuildRequest(occurredAt: DateTimeOffset.UtcNow.AddDays(-10)));

        var results = await _sut.QueryAsync(OwnerA, null, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), 100);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_OrdersByMostRecentFirst()
    {
        var older = await _sut.RecordAsync(BuildRequest(occurredAt: DateTimeOffset.UtcNow.AddHours(-2)));
        var newer = await _sut.RecordAsync(BuildRequest(occurredAt: DateTimeOffset.UtcNow.AddHours(-1)));

        var results = await _sut.QueryAsync(OwnerA, null, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), 100);

        results.Should().HaveCount(2);
        results[0].Id.Should().Be(newer.Value.Id);
        results[1].Id.Should().Be(older.Value.Id);
    }
}
