using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Analytics;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure.Analytics;

public sealed class SqliteDriftResultCacheTests : IDisposable
{
    private const string OwnerId = "owner-1";

    private readonly SqliteConnection _connection;
    private readonly DlqDbContext _dbContext;
    private readonly SqliteDriftResultCache _sut;

    public SqliteDriftResultCacheTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new SqliteDriftResultCache(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private static DriftFinding CreateFinding() =>
        DriftFinding.Create(Guid.NewGuid(), "test-queue", DriftFindingType.SchemaShapeDrift, 60, "shape drift");

    [Fact]
    public async Task TryGetAsync_UnknownId_ReturnsNull()
    {
        (await _sut.TryGetAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task StoreAsync_ThenTryGetAsync_ReturnsTheSameFinding()
    {
        var finding = CreateFinding();

        await _sut.StoreAsync(OwnerId, [finding]);

        var retrieved = await _sut.TryGetAsync(finding.Id);
        retrieved.Should().BeEquivalentTo(finding);
    }

    [Fact]
    public async Task StoreAsync_MultipleFindings_AllRetrievable()
    {
        var f1 = CreateFinding();
        var f2 = CreateFinding();

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

    [Fact]
    public async Task StoreAsync_SurvivesANewDbContextInstance_UnlikeTheProcessLocalCacheItReplaced()
    {
        // The whole point of M1: a finding must be resolvable through a fresh DbContext/scope,
        // not merely through the same in-process object that stored it.
        var finding = CreateFinding();
        await _sut.StoreAsync(OwnerId, [finding]);

        var options = new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(_connection).Options;
        await using var freshContext = new DlqDbContext(options);
        var freshCache = new SqliteDriftResultCache(freshContext);

        (await freshCache.TryGetAsync(finding.Id)).Should().BeEquivalentTo(finding);
    }
}
