using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure.Persistence;

public sealed class GovernanceGrantSeederTests : IDisposable
{
    private readonly DlqDbContext _dbContext;

    public GovernanceGrantSeederTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static Namespace BuildNamespace(string ownerId, string name) =>
        Namespace.Create(
            name,
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=servicehub;SharedAccessKey=dGVzdGtleQ==",
            ownerId: ownerId).Value;

    [Fact]
    public async Task SeedIfEmptyAsync_SeedsOneAdminGrantPerDistinctOwnerId()
    {
        _dbContext.Namespaces.Add(BuildNamespace("owner-1", "ns-1"));
        _dbContext.Namespaces.Add(BuildNamespace("owner-2", "ns-2"));
        await _dbContext.SaveChangesAsync();

        await GovernanceGrantSeeder.SeedIfEmptyAsync(_dbContext, NullLogger.Instance);

        var grants = await _dbContext.GovernanceGrants.ToListAsync();
        grants.Should().HaveCount(2);
        grants.Should().OnlyContain(g => g.Role == GovernanceRole.Admin && g.NamespaceId == null && g.PillarKind == null);
        grants.Select(g => g.OwnerId).Should().BeEquivalentTo(["owner-1", "owner-2"]);
        grants.Select(g => g.GranteeIdentity).Should().BeEquivalentTo(["owner-1", "owner-2"]);
    }

    [Fact]
    public async Task SeedIfEmptyAsync_SeedsOneOperatorGrantPerNamespaceSharedOwnerRow()
    {
        var ns = BuildNamespace("owner-1", "shared-ns");
        _dbContext.Namespaces.Add(ns);
        _dbContext.NamespaceSharedOwners.Add(new NamespaceSharedOwner { NamespaceId = ns.Id, OwnerId = "owner-2" });
        await _dbContext.SaveChangesAsync();

        await GovernanceGrantSeeder.SeedIfEmptyAsync(_dbContext, NullLogger.Instance);

        var operatorGrant = await _dbContext.GovernanceGrants.SingleAsync(g => g.Role == GovernanceRole.Operator);
        operatorGrant.OwnerId.Should().Be("owner-1", "the grant belongs to the namespace owner's partition");
        operatorGrant.GranteeIdentity.Should().Be("owner-2");
        operatorGrant.NamespaceId.Should().Be(ns.Id);
    }

    [Fact]
    public async Task SeedIfEmptyAsync_ApiKeyFormattedOwnerId_ClassifiedAsApiKeyGrantee()
    {
        _dbContext.Namespaces.Add(BuildNamespace("key_abc123", "ns-1"));
        await _dbContext.SaveChangesAsync();

        await GovernanceGrantSeeder.SeedIfEmptyAsync(_dbContext, NullLogger.Instance);

        var grant = await _dbContext.GovernanceGrants.SingleAsync();
        grant.GranteeKind.Should().Be(GranteeKind.ApiKey);
    }

    [Fact]
    public async Task SeedIfEmptyAsync_AlreadyPopulated_SkipsEntirely()
    {
        _dbContext.GovernanceGrants.Add(new GovernanceGrant
        {
            OwnerId = "owner-1",
            GranteeIdentity = "owner-1",
            GranteeKind = GranteeKind.User,
            Role = GovernanceRole.Admin,
            GrantedAt = DateTimeOffset.UtcNow,
            GrantedByIdentity = "test",
        });
        await _dbContext.SaveChangesAsync();

        _dbContext.Namespaces.Add(BuildNamespace("owner-2", "ns-2"));
        await _dbContext.SaveChangesAsync();

        await GovernanceGrantSeeder.SeedIfEmptyAsync(_dbContext, NullLogger.Instance);

        (await _dbContext.GovernanceGrants.CountAsync()).Should().Be(1, "must never re-seed once any grant exists");
    }

    [Fact]
    public async Task SeedIfEmptyAsync_NoNamespacesOrRules_SeedsNothing()
    {
        await GovernanceGrantSeeder.SeedIfEmptyAsync(_dbContext, NullLogger.Instance);

        (await _dbContext.GovernanceGrants.CountAsync()).Should().Be(0);
    }
}
