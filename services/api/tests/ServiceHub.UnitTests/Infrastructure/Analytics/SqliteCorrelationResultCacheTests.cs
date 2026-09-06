using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Analytics;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure.Analytics;

public sealed class SqliteCorrelationResultCacheTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DlqDbContext _dbContext;
    private readonly SqliteCorrelationResultCache _sut;

    public SqliteCorrelationResultCacheTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new SqliteCorrelationResultCache(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private static CorrelationFinding CreateFinding() =>
        CorrelationFinding.Create(
            "owner-1",
            [new CorrelationMember(Guid.NewGuid(), "test-queue", AnomalyType.HighMessageVolume, 60, CloudProviderType.Azure)],
            60,
            "correlated anomaly");

    [Fact]
    public async Task TryGetAsync_UnknownId_ReturnsNull()
    {
        (await _sut.TryGetAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task StoreAsync_ThenTryGetAsync_ReturnsTheSameFinding()
    {
        var finding = CreateFinding();

        await _sut.StoreAsync([finding]);

        (await _sut.TryGetAsync(finding.Id)).Should().BeEquivalentTo(finding);
    }

    [Fact]
    public async Task StoreAsync_MultipleFindings_AllRetrievable()
    {
        var f1 = CreateFinding();
        var f2 = CreateFinding();

        await _sut.StoreAsync([f1, f2]);

        (await _sut.TryGetAsync(f1.Id)).Should().BeEquivalentTo(f1);
        (await _sut.TryGetAsync(f2.Id)).Should().BeEquivalentTo(f2);
    }

    [Fact]
    public async Task StoreAsync_NullArgument_Throws()
    {
        var act = () => _sut.StoreAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
