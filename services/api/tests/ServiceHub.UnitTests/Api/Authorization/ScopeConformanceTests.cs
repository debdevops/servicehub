using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Routing;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Controllers;
using ServiceHub.Shared.Constants;

namespace ServiceHub.UnitTests.Api.Authorization;

/// <summary>
/// Conformance pack for API scope enforcement (v3.6.0 P2-3).
/// </summary>
/// <remarks>
/// <para>
/// Scoped API keys are only meaningful if every protected action actually declares what it
/// requires. Two endpoints — <c>FleetController.GetOverviewAsync</c> (fleet-wide DLQ counts) and
/// <c>FailureIntelligenceCenterController.GetInvestigationCenterAsync</c> (investigation queue,
/// failed replays, knowledge gaps) — shipped with no <c>[RequireScope]</c> at all, so a key
/// deliberately restricted to, say, <c>audit:read</c> could read full DLQ intelligence.
/// </para>
/// <para>
/// Fixing those two attributes is the small half. This test is the durable half: a new action
/// added without a scope fails the build rather than quietly widening what a restricted key can
/// reach. Deliberate exemptions must be added to <see cref="IntentionallyUnscopedActions"/>, which
/// forces the reasoning to be written down and reviewed rather than inferred from absence.
/// </para>
/// </remarks>
public sealed class ScopeConformanceTests
{
    /// <summary>
    /// Actions that intentionally carry no scope requirement, each with the reason it is safe.
    /// Keyed as "ControllerName.MethodName". Adding an entry here is a deliberate security
    /// decision — it should be justified in review, not added to make this test pass.
    /// </summary>
    private static readonly Dictionary<string, string> IntentionallyUnscopedActions = new()
    {
        ["MeController.Get"] =
            "Returns only the caller's own identity. Any successfully authenticated caller may "
            + "read who they are regardless of which scopes they were granted; gating this behind "
            + "a scope would make a key unable to discover its own effective permissions. The "
            + "rationale is recorded in the controller's class comment.",
    };

    private static IEnumerable<(Type Controller, MethodInfo Action)> GetAllControllerActions()
    {
        var apiAssembly = typeof(ApiControllerBase).Assembly;

        var controllers = apiAssembly.GetTypes()
            .Where(t => typeof(ApiControllerBase).IsAssignableFrom(t)
                        && !t.IsAbstract
                        && t.IsPublic);

        foreach (var controller in controllers)
        {
            var actions = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any());

            foreach (var action in actions)
            {
                yield return (controller, action);
            }
        }
    }

    [Fact]
    public void EveryControllerActionEitherRequiresAScopeOrIsExplicitlyExempt()
    {
        var violations = new List<string>();

        foreach (var (controller, action) in GetAllControllerActions())
        {
            var key = $"{controller.Name}.{action.Name}";

            var hasScope = action.GetCustomAttribute<RequireScopeAttribute>() is not null
                           || controller.GetCustomAttribute<RequireScopeAttribute>() is not null;

            if (hasScope || IntentionallyUnscopedActions.ContainsKey(key))
            {
                continue;
            }

            violations.Add(key);
        }

        violations.Should().BeEmpty(
            "every protected API action must declare its required scope with [RequireScope], or be "
            + "added to ScopeConformanceTests.IntentionallyUnscopedActions with a written reason. "
            + "Unscoped actions are reachable by any authenticated key regardless of how narrowly "
            + "that key was scoped. Offending action(s): {0}",
            string.Join(", ", violations));
    }

    [Fact]
    public void EveryDeclaredScopeIsAKnownScopeConstant()
    {
        // Guards against a typo'd literal ("dlq:reed") silently creating a scope no key can ever
        // hold, which would deny every caller rather than authorize the intended ones.
        var knownScopes = typeof(ApiKeyScopes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        knownScopes.Should().NotBeEmpty("the scope constants must be discoverable by reflection");

        var unknown = new List<string>();

        foreach (var (controller, action) in GetAllControllerActions())
        {
            var attribute = action.GetCustomAttribute<RequireScopeAttribute>()
                            ?? controller.GetCustomAttribute<RequireScopeAttribute>();

            if (attribute is not null && !knownScopes.Contains(attribute.Scope))
            {
                unknown.Add($"{controller.Name}.{action.Name} -> '{attribute.Scope}'");
            }
        }

        unknown.Should().BeEmpty(
            "every [RequireScope] value must be one of the ApiKeyScopes constants. Offender(s): {0}",
            string.Join(", ", unknown));
    }

    [Fact]
    public void TheExemptionListDoesNotNameActionsThatNoLongerExist()
    {
        // Keeps the allow-list honest: a stale entry could silently exempt a future action that
        // happens to reuse the same controller/method name.
        var existing = GetAllControllerActions()
            .Select(x => $"{x.Controller.Name}.{x.Action.Name}")
            .ToHashSet(StringComparer.Ordinal);

        var stale = IntentionallyUnscopedActions.Keys.Where(k => !existing.Contains(k)).ToList();

        stale.Should().BeEmpty(
            "the unscoped-action allow-list must not reference actions that no longer exist. "
            + "Stale entr(ies): {0}",
            string.Join(", ", stale));
    }

    [Fact]
    public void TheTwoPreviouslyUnscopedIntelligenceEndpointsNowRequireDlqRead()
    {
        // Pins the specific v3.6.0 P2-3 fix so a future refactor cannot quietly drop it back to
        // unscoped while still satisfying the generic conformance test via the allow-list.
        var actions = GetAllControllerActions().ToList();

        void AssertRequires(string controllerName, string expectedScope)
        {
            var match = actions.Where(a => a.Controller.Name == controllerName).ToList();
            match.Should().NotBeEmpty($"{controllerName} must still expose at least one action");

            foreach (var (controller, action) in match)
            {
                var attribute = action.GetCustomAttribute<RequireScopeAttribute>()
                                ?? controller.GetCustomAttribute<RequireScopeAttribute>();

                attribute.Should().NotBeNull(
                    $"{controllerName}.{action.Name} exposes DLQ intelligence and must be scoped");
                attribute!.Scope.Should().Be(expectedScope);
            }
        }

        AssertRequires("FleetController", ApiKeyScopes.DlqRead);
        AssertRequires("FailureIntelligenceCenterController", ApiKeyScopes.DlqRead);
    }

    [Fact]
    public void AtLeastOneControllerActionWasDiscovered()
    {
        // A reflection test that silently discovers nothing would pass forever while asserting
        // nothing. This is the canary for that.
        GetAllControllerActions().Should().HaveCountGreaterThan(50,
            "the API exposes dozens of actions; discovering far fewer means the reflection "
            + "predicate has drifted and the conformance checks above are vacuous");
    }
}
