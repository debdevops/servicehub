using FluentAssertions;
using ServiceHub.Core.Components;
using ServiceHub.Core.Enums;

namespace ServiceHub.UnitTests.Components;

/// <summary>The Add a component dialog is built from these rules, so they must cover every entry it can list.</summary>
public sealed class ComponentAccessRulesTests
{
    public static IEnumerable<object[]> Clouds() => Enum.GetValues<CloudKind>().Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(Clouds))]
    public void Every_component_a_person_can_tick_has_written_prerequisites(CloudKind cloud)
    {
        foreach (var row in ComponentAccessRules.Listed(cloud).Where(r => ComponentAccessRules.Of(r) is ComponentAccess.SameConnection or ComponentAccess.OwnCredential))
        {
            var setup = ComponentAccessRules.SetupFor(row.ComponentKey);
            setup.Should().NotBeNull($"{row.Name} can be ticked, so the dialog must say what it needs");
            setup!.Needs.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void A_component_read_on_demand_names_the_permissions_its_check_tests_and_a_step_to_run_it()
    {
        foreach (var row in ComponentCatalog.All.Where(r => r.Detection == DetectionKind.Runs && ComponentAccessRules.Of(r) == ComponentAccess.SameConnection))
        {
            var setup = ComponentAccessRules.SetupFor(row.ComponentKey)!;
            setup.Permissions.Should().NotBeEmpty($"{row.Name}: Check access reports against these");
            setup.Steps.Should().Contain(s => s.Contains("Check access"), "the last step is always the check");
        }
    }

    [Fact]
    public void Included_components_point_at_a_component_that_exists_and_is_listed_itself()
    {
        foreach (var (key, setup) in ComponentAccessRules.Setups.Where(s => s.Value.IncludedIn is not null))
        {
            ComponentCatalog.All.Should().Contain(c => c.ComponentKey == setup.IncludedIn, $"{key} is read as {setup.IncludedIn}");
        }
    }

    [Fact]
    public void Only_a_supported_message_store_is_watched_without_a_tick()
    {
        var always = ComponentCatalog.All.Where(ComponentAccessRules.IsAlwaysOn).Select(c => c.ComponentKey).Distinct().Order().ToList();

        always.Should().Equal("pubsub", "service-bus", "sns", "sqs");
        ComponentCatalog.All.Where(c => c.Detection == DetectionKind.Runs).Should().OnlyContain(c => !ComponentAccessRules.IsAlwaysOn(c),
            "a run history is read on demand, so it is ticked, never assumed");
    }

    [Fact]
    public void A_row_with_no_adapter_or_not_built_is_never_tickable()
    {
        ComponentCatalog.All.Where(c => c.Status == SupportStatus.OnRequest || c.Provider is null)
            .Where(c => ComponentAccessRules.SetupFor(c.ComponentKey)?.IncludedIn is null)
            .Should().OnlyContain(c => ComponentAccessRules.Of(c) == ComponentAccess.NotBuildable);
    }
}
