using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Analytics;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure.Analytics;

public sealed class SqliteAnomalyResultCacheTests : IDisposable
{
    private const string OwnerId = "owner-1";

    private readonly SqliteConnection _connection;
    private readonly DlqDbContext _dbContext;
    private readonly SqliteAnomalyResultCache _sut;

    public SqliteAnomalyResultCacheTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new SqliteAnomalyResultCache(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private static Anomaly CreateAnomaly() =>
        Anomaly.Create(Guid.NewGuid(), "test-queue", AnomalyType.HighMessageVolume, 60, "volume spike");

    [Fact]
    public async Task TryGetAsync_UnknownId_ReturnsNull()
    {
        (await _sut.TryGetAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task StoreAsync_ThenTryGetAsync_ReturnsTheSameAnomaly()
    {
        var anomaly = CreateAnomaly();

        await _sut.StoreAsync(OwnerId, [anomaly]);

        (await _sut.TryGetAsync(anomaly.Id)).Should().BeEquivalentTo(anomaly);
    }

    [Fact]
    public async Task StoreAsync_MultipleAnomalies_AllRetrievable()
    {
        var a1 = CreateAnomaly();
        var a2 = CreateAnomaly();

        await _sut.StoreAsync(OwnerId, [a1, a2]);

        (await _sut.TryGetAsync(a1.Id)).Should().BeEquivalentTo(a1);
        (await _sut.TryGetAsync(a2.Id)).Should().BeEquivalentTo(a2);
    }

    [Fact]
    public async Task StoreAsync_NullArgument_Throws()
    {
        var act = () => _sut.StoreAsync(OwnerId, null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
