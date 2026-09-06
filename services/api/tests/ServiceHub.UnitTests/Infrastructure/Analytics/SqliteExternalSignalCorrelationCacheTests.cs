using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Analytics;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure.Analytics;

public sealed class SqliteExternalSignalCorrelationCacheTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DlqDbContext _dbContext;
    private readonly SqliteExternalSignalCorrelationCache _sut;

    public SqliteExternalSignalCorrelationCacheTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new SqliteExternalSignalCorrelationCache(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private static ExternalSignalCorrelation CreateCorrelation()
    {
        var signal = new ExternalSignalEvent
        {
            OwnerId = "owner-1",
            NamespaceId = Guid.NewGuid(),
            SignalType = ExternalSignalType.Deploy,
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Source = "ci-pipeline",
            IngestedAt = DateTimeOffset.UtcNow,
        };

        return ExternalSignalCorrelation.Create(
            "owner-1",
            signal.NamespaceId!.Value,
            "test-queue",
            AnomalyType.HighMessageVolume,
            60,
            CloudProviderType.Azure,
            signal,
            TimeSpan.FromMinutes(5),
            "correlated with a recent deploy");
    }

    [Fact]
    public async Task TryGetAsync_UnknownId_ReturnsNull()
    {
        (await _sut.TryGetAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task StoreAsync_ThenTryGetAsync_ReturnsTheSameCorrelation()
    {
        var correlation = CreateCorrelation();

        await _sut.StoreAsync([correlation]);

        (await _sut.TryGetAsync(correlation.Id)).Should().BeEquivalentTo(correlation);
    }

    [Fact]
    public async Task StoreAsync_MultipleCorrelations_AllRetrievable()
    {
        var c1 = CreateCorrelation();
        var c2 = CreateCorrelation();

        await _sut.StoreAsync([c1, c2]);

        (await _sut.TryGetAsync(c1.Id)).Should().BeEquivalentTo(c1);
        (await _sut.TryGetAsync(c2.Id)).Should().BeEquivalentTo(c2);
    }

    [Fact]
    public async Task StoreAsync_NullArgument_Throws()
    {
        var act = () => _sut.StoreAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
