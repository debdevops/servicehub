using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure;

public sealed class GovernanceGrantServiceTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly Mock<IAuditService> _auditService = new();
    private readonly GovernanceGrantService _service;

    public GovernanceGrantServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _service = new GovernanceGrantService(_dbContext, _auditService.Object, NullLogger<GovernanceGrantService>.Instance);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static GrantRoleRequest BuildRequest(
        string ownerId = "owner-1",
        string granteeIdentity = "alex@contoso.com",
        GovernanceRole role = GovernanceRole.Operator,
        Guid? namespaceId = null,
        PillarKind? pillarKind = null) =>
        new(ownerId, granteeIdentity, GranteeKind.User, role, namespaceId, pillarKind, GrantedByIdentity: "admin@contoso.com");

    [Fact]
    public async Task GrantAsync_ValidRequest_CreatesGrantAndWritesAuditLog()
    {
        var result = await _service.GrantAsync(BuildRequest());

        result.IsSuccess.Should().BeTrue();
        result.Value.Role.Should().Be(GovernanceRole.Operator);
        result.Value.RevokedAt.Should().BeNull();

        (await _dbContext.GovernanceGrants.CountAsync()).Should().Be(1);
        _auditService.Verify(a => a.Enqueue(It.Is<AuditLog>(l => l.Action == "Governance.Grant")), Times.Once);
    }

    [Fact]
    public async Task GrantAsync_DuplicateFleetWideAllPillarScope_ReturnsConflict()
    {
        // The exact NULL/NULL case the database's own filtered unique index cannot catch — must
        // be enforced in code.
        await _service.GrantAsync(BuildRequest(namespaceId: null, pillarKind: null));
        var second = await _service.GrantAsync(BuildRequest(namespaceId: null, pillarKind: null));

        second.IsFailure.Should().BeTrue();
        (await _dbContext.GovernanceGrants.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GrantAsync_DuplicateNamespaceScopedScope_ReturnsConflict()
    {
        var namespaceId = Guid.NewGuid();
        await _service.GrantAsync(BuildRequest(namespaceId: namespaceId));
        var second = await _service.GrantAsync(BuildRequest(namespaceId: namespaceId));

        second.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task GrantAsync_DifferentNamespaceScopes_BothSucceed()
    {
        var first = await _service.GrantAsync(BuildRequest(namespaceId: Guid.NewGuid()));
        var second = await _service.GrantAsync(BuildRequest(namespaceId: Guid.NewGuid()));

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GrantAsync_SameGranteeDifferentOwnerPartition_BothSucceed()
    {
        var first = await _service.GrantAsync(BuildRequest(ownerId: "owner-1"));
        var second = await _service.GrantAsync(BuildRequest(ownerId: "owner-2"));

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeAsync_ActiveGrant_SetsRevokedAtAndWritesAuditLog_NeverDeletesRow()
    {
        var grant = (await _service.GrantAsync(BuildRequest())).Value;

        var result = await _service.RevokeAsync(grant.Id, "owner-1", "admin@contoso.com");

        result.IsSuccess.Should().BeTrue();
        (await _dbContext.GovernanceGrants.CountAsync()).Should().Be(1, "revocation must never delete the row");

        var reloaded = await _dbContext.GovernanceGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id);
        reloaded.RevokedAt.Should().NotBeNull();
        reloaded.RevokedByIdentity.Should().Be("admin@contoso.com");

        _auditService.Verify(a => a.Enqueue(It.Is<AuditLog>(l => l.Action == "Governance.Revoke")), Times.Once);
    }

    [Fact]
    public async Task RevokeAsync_AlreadyRevoked_IsIdempotent()
    {
        var grant = (await _service.GrantAsync(BuildRequest())).Value;
        await _service.RevokeAsync(grant.Id, "owner-1", "admin@contoso.com");

        var second = await _service.RevokeAsync(grant.Id, "owner-1", "someone-else@contoso.com");

        second.IsSuccess.Should().BeTrue();
        var reloaded = await _dbContext.GovernanceGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id);
        reloaded.RevokedByIdentity.Should().Be("admin@contoso.com", "a no-op re-revoke must not overwrite who actually revoked it");
    }

    [Fact]
    public async Task RevokeAsync_NonExistentGrant_ReturnsNotFound()
    {
        var result = await _service.RevokeAsync(Guid.NewGuid(), "owner-1", "admin@contoso.com");
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeAsync_WrongOwnerPartition_ReturnsNotFound()
    {
        var grant = (await _service.GrantAsync(BuildRequest(ownerId: "owner-1"))).Value;

        var result = await _service.RevokeAsync(grant.Id, "owner-2", "admin@contoso.com");
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task RevokedGrant_CanBeReGrantedForTheSameScope()
    {
        var grant = (await _service.GrantAsync(BuildRequest())).Value;
        await _service.RevokeAsync(grant.Id, "owner-1", "admin@contoso.com");

        var regrant = await _service.GrantAsync(BuildRequest());

        regrant.IsSuccess.Should().BeTrue();
        (await _dbContext.GovernanceGrants.CountAsync()).Should().Be(2, "revoke-then-grant creates a new row rather than reviving the old one");
    }

    [Fact]
    public async Task GetActiveGrantsAsync_ExcludesRevokedGrants()
    {
        var active = (await _service.GrantAsync(BuildRequest(granteeIdentity: "active@contoso.com"))).Value;
        var revoked = (await _service.GrantAsync(BuildRequest(granteeIdentity: "revoked@contoso.com", namespaceId: Guid.NewGuid()))).Value;
        await _service.RevokeAsync(revoked.Id, "owner-1", "admin@contoso.com");

        var result = await _service.GetActiveGrantsAsync("owner-1");

        result.Value.Should().ContainSingle(g => g.Id == active.Id);
    }

    [Fact]
    public async Task GetGrantsForGranteeAsync_ScopesToOwnerAndGrantee()
    {
        await _service.GrantAsync(BuildRequest(ownerId: "owner-1", granteeIdentity: "alex@contoso.com"));
        await _service.GrantAsync(BuildRequest(ownerId: "owner-1", granteeIdentity: "sam@contoso.com"));

        var result = await _service.GetGrantsForGranteeAsync("owner-1", "alex@contoso.com");

        result.Value.Should().ContainSingle(g => g.GranteeIdentity == "alex@contoso.com");
    }
}
