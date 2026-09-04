using FluentAssertions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Governance;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.Governance;

public sealed class GovernanceAccessEvaluatorTests
{
    private const string OwnerId = "entra:owner-a";
    private const string GranteeIdentity = "ApiKey:ci-deploy";

    private readonly Mock<IGovernanceGrantService> _governanceGrantService = new();
    private readonly GovernanceAccessEvaluator _evaluator;

    public GovernanceAccessEvaluatorTests()
    {
        _evaluator = new GovernanceAccessEvaluator(
            _governanceGrantService.Object, Mock.Of<Microsoft.Extensions.Logging.ILogger<GovernanceAccessEvaluator>>());
    }

    private void SeedActiveGrants(params GovernanceGrant[] grants)
    {
        _governanceGrantService
            .Setup(s => s.GetActiveGrantsAsync(OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<GovernanceGrant>>(grants));
    }

    private static GovernanceGrant MakeGrant(
        string granteeIdentity, GovernanceRole role, Guid? namespaceId = null, PillarKind? pillarKind = null) => new()
    {
        OwnerId = OwnerId,
        GranteeIdentity = granteeIdentity,
        GranteeKind = GranteeKind.User,
        Role = role,
        NamespaceId = namespaceId,
        PillarKind = pillarKind,
        GrantedAt = DateTimeOffset.UtcNow,
        GrantedByIdentity = "test",
    };

    [Fact]
    public async Task EvaluateAsync_OwnerHasZeroGrants_AllowsAnyRole()
    {
        SeedActiveGrants();

        var result = await _evaluator.EvaluateAsync(OwnerId, GranteeIdentity, GovernanceRole.Admin, null, null);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_GranteeHasNoMatchingGrant_Denies()
    {
        SeedActiveGrants(MakeGrant("someone-else", GovernanceRole.Admin));

        var result = await _evaluator.EvaluateAsync(OwnerId, GranteeIdentity, GovernanceRole.Viewer, null, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Governance.InsufficientRole");
    }

    [Fact]
    public async Task EvaluateAsync_OwnerLevelFallbackGrant_AppliesToAnyIdentityUnderThatOwner()
    {
        // GovernanceGrantSeeder grandfathers a fleet-wide Admin grant with GranteeIdentity ==
        // OwnerId — any resolved actor identity under that owner should still match it.
        SeedActiveGrants(MakeGrant(OwnerId, GovernanceRole.Admin));

        var result = await _evaluator.EvaluateAsync(OwnerId, GranteeIdentity, GovernanceRole.Admin, null, null);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_OwnerLevelFallbackGrant_DoesNotOverridePerIdentityGrant_OnceIdentityIsDifferentiated()
    {
        // Live-verified 2026-09-04 against a real deployment: GovernanceGrantSeeder always seeds
        // a fleet-wide Admin grant with GranteeIdentity == OwnerId ("__spa__") at first boot. Once
        // an admin has *also* granted this specific identity a narrower role (Viewer, here), that
        // narrower grant must be authoritative — the caller must not still inherit Admin via the
        // owner-level grandfather grant, or per-identity restriction (W3.1/W3.2) is a no-op for
        // every real deployment's primary owner.
        SeedActiveGrants(
            MakeGrant(OwnerId, GovernanceRole.Admin),
            MakeGrant(GranteeIdentity, GovernanceRole.Viewer, namespaceId: null, pillarKind: PillarKind.Recover));

        var result = await _evaluator.EvaluateAsync(
            OwnerId, GranteeIdentity, GovernanceRole.Operator, namespaceId: null, pillarKind: PillarKind.Recover);

        result.IsFailure.Should().BeTrue(
            "a Viewer-only grant for this specific identity must not be topped up by the owner-level Admin grant");
        result.Error.Code.Should().Be("Governance.InsufficientRole");
    }

    [Fact]
    public async Task EvaluateAsync_ExactIdentityGrantMeetsRequiredRole_Allows()
    {
        SeedActiveGrants(MakeGrant(GranteeIdentity, GovernanceRole.Operator));

        var result = await _evaluator.EvaluateAsync(OwnerId, GranteeIdentity, GovernanceRole.Operator, null, null);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_GrantBelowRequiredRole_Denies()
    {
        SeedActiveGrants(MakeGrant(GranteeIdentity, GovernanceRole.Viewer));

        var result = await _evaluator.EvaluateAsync(OwnerId, GranteeIdentity, GovernanceRole.Operator, null, null);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_GrantScopedToDifferentNamespace_DoesNotApply()
    {
        var otherNamespace = Guid.NewGuid();
        var requestedNamespace = Guid.NewGuid();
        SeedActiveGrants(MakeGrant(GranteeIdentity, GovernanceRole.Admin, namespaceId: otherNamespace));

        var result = await _evaluator.EvaluateAsync(OwnerId, GranteeIdentity, GovernanceRole.Viewer, requestedNamespace, null);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_FleetWideGrant_AppliesToAnyNamespace()
    {
        SeedActiveGrants(MakeGrant(GranteeIdentity, GovernanceRole.Operator, namespaceId: null));

        var result = await _evaluator.EvaluateAsync(OwnerId, GranteeIdentity, GovernanceRole.Operator, Guid.NewGuid(), PillarKind.Recover);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_GrantScopedToDifferentPillar_DoesNotApply()
    {
        SeedActiveGrants(MakeGrant(GranteeIdentity, GovernanceRole.Admin, pillarKind: PillarKind.Prevent));

        var result = await _evaluator.EvaluateAsync(OwnerId, GranteeIdentity, GovernanceRole.Viewer, null, PillarKind.Recover);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_MultipleApplicableGrants_UsesHighestRole()
    {
        SeedActiveGrants(
            MakeGrant(GranteeIdentity, GovernanceRole.Viewer),
            MakeGrant(GranteeIdentity, GovernanceRole.Approver, pillarKind: PillarKind.Investigate));

        var result = await _evaluator.EvaluateAsync(OwnerId, GranteeIdentity, GovernanceRole.Approver, null, PillarKind.Investigate);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_GrantStoreReadFails_FailsClosed()
    {
        _governanceGrantService
            .Setup(s => s.GetActiveGrantsAsync(OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IReadOnlyList<GovernanceGrant>>(Error.Internal("Boom", "db down")));

        var result = await _evaluator.EvaluateAsync(OwnerId, GranteeIdentity, GovernanceRole.Viewer, null, null);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task GetEffectiveRoleAsync_OwnerHasZeroGrants_ReturnsAdmin()
    {
        SeedActiveGrants();

        var role = await _evaluator.GetEffectiveRoleAsync(OwnerId, GranteeIdentity, null, null);

        role.Should().Be(GovernanceRole.Admin);
    }

    [Fact]
    public async Task GetEffectiveRoleAsync_NoMatchingGrant_ReturnsNull()
    {
        SeedActiveGrants(MakeGrant("someone-else", GovernanceRole.Admin));

        var role = await _evaluator.GetEffectiveRoleAsync(OwnerId, GranteeIdentity, null, null);

        role.Should().BeNull();
    }

    [Fact]
    public async Task GetEffectiveRoleAsync_MatchingGrant_ReturnsItsRole()
    {
        SeedActiveGrants(MakeGrant(GranteeIdentity, GovernanceRole.Operator));

        var role = await _evaluator.GetEffectiveRoleAsync(OwnerId, GranteeIdentity, null, null);

        role.Should().Be(GovernanceRole.Operator);
    }

    [Fact]
    public async Task EvaluateAsync_RevokedGrantExcludedByServiceLayer_NotConsidered()
    {
        // GetActiveGrantsAsync only ever returns non-revoked rows (enforced by
        // IGovernanceGrantService itself) — a mock returning an empty list simulates a fully
        // revoked grant, which must fall back to bootstrap-inactive behaviour, not a stale grant.
        SeedActiveGrants();

        var result = await _evaluator.EvaluateAsync(OwnerId, GranteeIdentity, GovernanceRole.Admin, null, null);

        result.IsSuccess.Should().BeTrue();
    }
}
