using FluentAssertions;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.UnitTests.Infrastructure.RecoveryLedger;

public sealed class ActorIdentityResolverTests
{
    [Fact]
    public void ResolveHttpActor_ApiKeyPresent_WinsOverEverythingElse()
    {
        var actor = ActorIdentityResolver.ResolveHttpActor(
            apiKeyName: "ops-bot", claimsIdentityName: "alice@example.com", ownerId: "owner-a");

        actor.Identity.Should().Be("ApiKey:ops-bot");
        actor.Kind.Should().Be(RecoveryActorKind.ApiKey);
    }

    [Fact]
    public void ResolveHttpActor_NoApiKey_ClaimsIdentityWinsOverOwnerId()
    {
        var actor = ActorIdentityResolver.ResolveHttpActor(
            apiKeyName: null, claimsIdentityName: "alice@example.com", ownerId: "owner-a");

        actor.Identity.Should().Be("alice@example.com");
        actor.Kind.Should().Be(RecoveryActorKind.User);
    }

    [Fact]
    public void ResolveHttpActor_NoApiKeyOrClaims_FallsBackToOwnerId()
    {
        var actor = ActorIdentityResolver.ResolveHttpActor(
            apiKeyName: null, claimsIdentityName: null, ownerId: "owner-a");

        actor.Identity.Should().Be("owner-a");
        actor.Kind.Should().Be(RecoveryActorKind.User);
    }

    [Fact]
    public void ResolveHttpActor_NothingAvailable_ResolvesToUnknown()
    {
        var actor = ActorIdentityResolver.ResolveHttpActor(
            apiKeyName: null, claimsIdentityName: null, ownerId: null);

        actor.Identity.Should().Be("Unknown");
        actor.Kind.Should().Be(RecoveryActorKind.User);
    }

    [Fact]
    public void ResolveHttpActor_EmptyApiKeyName_IsTreatedAsAbsent()
    {
        // Mirrors SecurityAuditLogger.ResolveUserIdentity's precedence exactly, including its
        // use of IsNullOrEmpty (not IsNullOrWhiteSpace) — an empty string falls through to the
        // next precedence tier, same as that existing method.
        var actor = ActorIdentityResolver.ResolveHttpActor(
            apiKeyName: "", claimsIdentityName: "alice@example.com", ownerId: "owner-a");

        actor.Kind.Should().Be(RecoveryActorKind.User);
        actor.Identity.Should().Be("alice@example.com");
    }

    [Fact]
    public void ResolveHttpActor_Scopes_ArePassedThrough()
    {
        var actor = ActorIdentityResolver.ResolveHttpActor(
            apiKeyName: "ops-bot", claimsIdentityName: null, ownerId: null, scopes: "recovery:read recovery:write");

        actor.Scopes.Should().Be("recovery:read recovery:write");
    }

    [Fact]
    public void ResolveAutomationActor_FormatsAsKindColonIdAtName()
    {
        var actor = ActorIdentityResolver.ResolveAutomationActor("Rule", "42", "drain-poison-queue");

        actor.Identity.Should().Be("Rule:42@drain-poison-queue");
        actor.Kind.Should().Be(RecoveryActorKind.Automation);
    }

    [Fact]
    public void ResolveSystemActor_FormatsWithSystemPrefix()
    {
        var actor = ActorIdentityResolver.ResolveSystemActor("StartupRecovery");

        actor.Identity.Should().Be("System:StartupRecovery");
        actor.Kind.Should().Be(RecoveryActorKind.System);
    }
}
