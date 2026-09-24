using FluentAssertions;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Identity;

namespace ServiceHub.UnitTests.Identity;

/// <summary>
/// Rule R6, asserted: three honest outcomes, and the resolver never invents a person.
/// </summary>
public sealed class ActorIdentityResolverTests
{
    private readonly ActorIdentityResolver _resolver = new();

    [Fact]
    public void With_nothing_configured_the_actor_is_the_browser_session_and_says_only_that()
    {
        var actor = _resolver.Resolve(new ActorContext());

        actor.Identity.Should().Be("session");
        actor.Kind.Should().Be(RecoveryActorKind.User);
        RecoveryActorLabel.For(actor.Identity).Should().Be("from this browser session");
        RecoveryActorLabel.IsSession(actor.Identity).Should().BeTrue();
    }

    [Fact]
    public void A_session_id_is_kept_but_never_becomes_a_name()
    {
        var actor = _resolver.Resolve(new ActorContext(SessionId: "3f2b9c1e-7a64-4d0a-9d5e-0a1b2c3d4e5f"));

        actor.Identity.Should().Be("session:3f2b9c1e-7a64-4d0a-9d5e-0a1b2c3d4e5f");
        RecoveryActorLabel.IsSession(actor.Identity).Should().BeTrue();
        RecoveryActorLabel.For(actor.Identity).Should().Be("from this browser session");
    }

    [Fact]
    public void A_claims_name_is_used_as_given()
    {
        var actor = _resolver.Resolve(new ActorContext(ClaimsName: " alice@example.com "));

        actor.Identity.Should().Be("alice@example.com");
        actor.Kind.Should().Be(RecoveryActorKind.User);
        RecoveryActorLabel.IsSession(actor.Identity).Should().BeFalse();
    }

    [Fact]
    public void An_api_key_is_named_and_visibly_a_credential()
    {
        var actor = _resolver.Resolve(new ActorContext(ApiKeyName: "ops-bot", Scopes: "dlq:read"));

        actor.Identity.Should().Be("ApiKey:ops-bot");
        actor.Kind.Should().Be(RecoveryActorKind.ApiKey);
        actor.Scopes.Should().Be("dlq:read");
    }

    [Fact]
    public void An_api_key_outranks_a_claims_name()
    {
        var actor = _resolver.Resolve(new ActorContext(ClaimsName: "alice@example.com", ApiKeyName: "ops-bot"));

        actor.Kind.Should().Be(RecoveryActorKind.ApiKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_names_are_treated_as_absent(string blank)
    {
        var actor = _resolver.Resolve(new ActorContext(ClaimsName: blank, ApiKeyName: blank));

        RecoveryActorLabel.IsSession(actor.Identity).Should().BeTrue();
    }

    [Fact]
    public void A_person_whose_name_merely_starts_with_session_is_not_mistaken_for_one()
    {
        RecoveryActorLabel.IsSession("sessionmaster@example.com").Should().BeFalse();
        RecoveryActorLabel.For("sessionmaster@example.com").Should().Be("sessionmaster@example.com");
    }
}
