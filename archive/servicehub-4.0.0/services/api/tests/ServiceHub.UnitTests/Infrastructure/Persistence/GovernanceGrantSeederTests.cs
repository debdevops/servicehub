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
    public async Task SeedIfEmptyAsync_OwnerAlreadyHasAGrant_IsNeverReseeded()
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

        _dbContext.Namespaces.Add(BuildNamespace("owner-1", "ns-1"));
        await _dbContext.SaveChangesAsync();

        await GovernanceGrantSeeder.SeedIfEmptyAsync(_dbContext, NullLogger.Instance);

        (await _dbContext.GovernanceGrants.CountAsync()).Should().Be(1, "an owner with an existing grant of its own must never be re-seeded");
    }

    [Fact]
    public async Task SeedIfEmptyAsync_NewOwnerArrivesAfterAnotherOwnerWasAlreadySeeded_StillGetsGrandfathered()
    {
        // Regression for a real, live self-lockout bug found during the 2026-09-19 E2E pass: the
        // seed used to be gated on "GovernanceGrants is empty" for the whole table, so an owner
        // that first appears (e.g. registers its own namespace) only after some earlier owner
        // already has grants never got its own grandfather Admin grant at all. That owner's
        // GovernanceAccessEvaluator bootstrap bypass then closed forever the instant its own
        // first grant was created, locking out every credential in that owner partition with no
        // way back in through the API.
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

        _dbContext.Namespaces.Add(BuildNamespace("owner-1", "ns-1"));
        _dbContext.Namespaces.Add(BuildNamespace("owner-2", "ns-2"));
        await _dbContext.SaveChangesAsync();

        await GovernanceGrantSeeder.SeedIfEmptyAsync(_dbContext, NullLogger.Instance);

        var grants = await _dbContext.GovernanceGrants.ToListAsync();
        grants.Should().HaveCount(2, "owner-1 keeps its one existing grant, and owner-2 — new, with none of its own — gets grandfathered");
        grants.Should().ContainSingle(g => g.OwnerId == "owner-2" && g.GranteeIdentity == "owner-2" && g.Role == GovernanceRole.Admin);
    }

    [Fact]
    public async Task SeedIfEmptyAsync_OwnerOnlyHasAGrantForSomeoneElse_StillGetsItsOwnGrandfatherGrant()
    {
        // Regression: the per-owner gate used to key on "any GovernanceGrant row naming this
        // OwnerId", not specifically a self-grant. An owner who granted a colleague Viewer while
        // still in the bootstrap-bypass window (zero grants of their own) would then be treated
        // as "already seeded" forever, because that colleague's row already carries this owner's
        // OwnerId — even though the owner itself never received the self-grant that actually
        // satisfies GovernanceAccessEvaluator's bootstrap check. That silently and permanently
        // locked the owner out the moment Governance activated for its partition, with no
        // self-service way back in (granting requires Admin).
        _dbContext.GovernanceGrants.Add(new GovernanceGrant
        {
            OwnerId = "owner-1",
            GranteeIdentity = "someone-else",
            GranteeKind = GranteeKind.User,
            Role = GovernanceRole.Viewer,
            GrantedAt = DateTimeOffset.UtcNow,
            GrantedByIdentity = "test",
        });
        await _dbContext.SaveChangesAsync();

        _dbContext.Namespaces.Add(BuildNamespace("owner-1", "ns-1"));
        await _dbContext.SaveChangesAsync();

        await GovernanceGrantSeeder.SeedIfEmptyAsync(_dbContext, NullLogger.Instance);

        var grants = await _dbContext.GovernanceGrants.ToListAsync();
        grants.Should().HaveCount(2, "the pre-existing grant for someone else stays, and owner-1 must still get its own grandfather grant");
        grants.Should().ContainSingle(g => g.OwnerId == "owner-1" && g.GranteeIdentity == "owner-1" && g.Role == GovernanceRole.Admin);
    }

    [Fact]
    public async Task SeedIfEmptyAsync_NoNamespacesOrRules_SeedsNothing()
    {
        await GovernanceGrantSeeder.SeedIfEmptyAsync(_dbContext, NullLogger.Instance);

        (await _dbContext.GovernanceGrants.CountAsync()).Should().Be(0);
    }
}
