using FluentAssertions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Governance;
using ServiceHub.Core.Results;

namespace ServiceHub.UnitTests.Governance;

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

        // Default: this identity has never been individually granted anything of its own (active
        // or revoked). Tests proving the "was differentiated, now has zero active grants" case
        // override this for the specific (OwnerId, GranteeIdentity) pair under test.
        _governanceGrantService
            .Setup(s => s.HasEverHadOwnGrantAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        // Default: this owner has never had any grant recorded at all — a genuinely fresh tenant.
        // Tests proving the "Governance was activated, then every grant was revoked" case override
        // this for the specific OwnerId under test.
        _governanceGrantService
            .Setup(s => s.HasAnyGrantEverAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));
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

    [Fact]
    public async Task EvaluateAsync_IdentityWasDifferentiatedThenItsGrantWasRevoked_NeverFallsBackToOwnerLevelGrant()
    {
        // Regression for a real, live privilege-escalation bug found during the 2026-09-19 E2E
        // pass: once an identity has had ANY grant of its own (even since revoked), it must never
        // again inherit the coarse owner-level grandfather grant — otherwise "revoke this
        // Viewer's access" silently becomes "this identity is unrestricted Admin again" the
        // moment its own active-grant list goes back to empty. Live-found: an API key scoped to
        // Viewer, after its one grant was revoked, successfully created a brand-new fleet-wide
        // Admin grant for an arbitrary identity through this exact path.
        SeedActiveGrants(MakeGrant(OwnerId, GovernanceRole.Admin)); // only the grandfather grant is still active
        _governanceGrantService
            .Setup(s => s.HasEverHadOwnGrantAsync(OwnerId, GranteeIdentity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true)); // this identity had a grant of its own, since revoked

        var result = await _evaluator.EvaluateAsync(OwnerId, GranteeIdentity, GovernanceRole.Admin, null, null);

        result.IsFailure.Should().BeTrue(
            "an identity that was ever individually differentiated must never fall back to the owner-level grant again, even after its own grant is revoked");
        result.Error.Code.Should().Be("Governance.InsufficientRole");
    }

    [Fact]
    public async Task EvaluateAsync_GrantHistoryReadFails_FailsClosed()
    {
        SeedActiveGrants(MakeGrant("someone-else", GovernanceRole.Admin));
        _governanceGrantService
            .Setup(s => s.HasEverHadOwnGrantAsync(OwnerId, GranteeIdentity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<bool>(Error.Internal("Boom", "db down")));

        var result = await _evaluator.EvaluateAsync(OwnerId, GranteeIdentity, GovernanceRole.Viewer, null, null);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_AllGrantsIncludingOwnerLevelSeedRevoked_DoesNotFallBackToUnrestricted()
    {
        // Regression: live-verified 2026-09-20 that revoking every remaining grant for an owner —
        // including the fleet-wide owner-level seed grant — was indistinguishable from a fresh
        // tenant that never had Governance activated, so ResolveAsync returned Inactive() and
        // EvaluateAsync granted unrestricted (Admin-equivalent) access to any grantee/role. Once
        // Governance has ever been activated for an owner, revoking down to zero active grants
        // must deny, not reopen unrestricted access.
        SeedActiveGrants();
        _governanceGrantService
            .Setup(s => s.HasAnyGrantEverAsync(OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var result = await _evaluator.EvaluateAsync(OwnerId, GranteeIdentity, GovernanceRole.Admin, null, null);

        result.IsFailure.Should().BeTrue(
            "an owner whose Governance was activated and then fully revoked must fail closed, not fall back to bootstrap-unrestricted");
        result.Error.Code.Should().Be("Governance.InsufficientRole");
    }

    [Fact]
    public async Task EvaluateAsync_OwnerGrantHistoryReadFails_FailsClosed()
    {
        SeedActiveGrants();
        _governanceGrantService
            .Setup(s => s.HasAnyGrantEverAsync(OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<bool>(Error.Internal("Boom", "db down")));

        var result = await _evaluator.EvaluateAsync(OwnerId, GranteeIdentity, GovernanceRole.Viewer, null, null);

        result.IsFailure.Should().BeTrue();
    }
}
