using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Analytics;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure.Analytics;

public sealed class SqliteNarrationResultCacheTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DlqDbContext _dbContext;
    private readonly SqliteNarrationResultCache _sut;

    public SqliteNarrationResultCacheTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new SqliteNarrationResultCache(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private static Narration CreateNarration()
    {
        var namespaceId = Guid.NewGuid();
        return Narration.Create(
            NarrationKind.NamespaceActivity,
            namespaceId,
            [namespaceId],
            "Headline",
            "Summary paragraph",
            60);
    }

    [Fact]
    public async Task TryGetAsync_UnknownId_ReturnsNull()
    {
        (await _sut.TryGetAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task StoreAsync_ThenTryGetAsync_ReturnsTheSameNarration()
    {
        var narration = CreateNarration();

        await _sut.StoreAsync([narration]);

        (await _sut.TryGetAsync(narration.Id)).Should().BeEquivalentTo(narration);
    }

    [Fact]
    public async Task StoreAsync_MultipleNarrations_AllRetrievable()
    {
        var n1 = CreateNarration();
        var n2 = CreateNarration();

        await _sut.StoreAsync([n1, n2]);

        (await _sut.TryGetAsync(n1.Id)).Should().BeEquivalentTo(n1);
        (await _sut.TryGetAsync(n2.Id)).Should().BeEquivalentTo(n2);
    }

    [Fact]
    public async Task StoreAsync_NullArgument_Throws()
    {
        var act = () => _sut.StoreAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
