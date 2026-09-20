using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Infrastructure.Analytics;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure.Analytics;

public sealed class SqliteBacklogForecastResultCacheTests : IDisposable
{
    private const string OwnerId = "owner-1";

    private readonly SqliteConnection _connection;
    private readonly DlqDbContext _dbContext;
    private readonly SqliteBacklogForecastResultCache _sut;

    public SqliteBacklogForecastResultCacheTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new SqliteBacklogForecastResultCache(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private static BacklogForecast CreateForecast() =>
        BacklogForecast.Create(Guid.NewGuid(), "test-queue", 500, 25.0, 1000, 20.0, 55, "projected breach");

    [Fact]
    public async Task TryGetAsync_UnknownId_ReturnsNull()
    {
        (await _sut.TryGetAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task StoreAsync_ThenTryGetAsync_ReturnsTheSameForecast()
    {
        var forecast = CreateForecast();

        await _sut.StoreAsync(OwnerId, [forecast]);

        (await _sut.TryGetAsync(forecast.Id)).Should().BeEquivalentTo(forecast);
    }

    [Fact]
    public async Task StoreAsync_MultipleForecasts_AllRetrievable()
    {
        var f1 = CreateForecast();
        var f2 = CreateForecast();

        await _sut.StoreAsync(OwnerId, [f1, f2]);

        (await _sut.TryGetAsync(f1.Id)).Should().BeEquivalentTo(f1);
        (await _sut.TryGetAsync(f2.Id)).Should().BeEquivalentTo(f2);
    }

    [Fact]
    public async Task StoreAsync_NullArgument_Throws()
    {
        var act = () => _sut.StoreAsync(OwnerId, null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
