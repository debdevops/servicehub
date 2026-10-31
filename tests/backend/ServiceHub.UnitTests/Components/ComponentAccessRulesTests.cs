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
        foreach (var row in ComponentCatalog.All.Where(r => ComponentAccessRules.ReadsOnDemand(r) && ComponentAccessRules.Of(r) == ComponentAccess.SameConnection))
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
        ComponentCatalog.All.Where(ComponentAccessRules.ReadsOnDemand).Should().OnlyContain(c => !ComponentAccessRules.IsAlwaysOn(c),
            "a run history is read on demand, so it is ticked, never assumed");
    }

    [Fact]
    public void A_row_with_no_adapter_or_not_built_is_never_tickable()
    {
        ComponentCatalog.All.Where(c => c.Status == SupportStatus.OnRequest || c.Provider is null)
            .Where(c => ComponentAccessRules.SetupFor(c.ComponentKey)?.IncludedIn is null)
            .Should().OnlyContain(c => ComponentAccessRules.Of(c) == ComponentAccess.NotBuildable);
    }

    private static readonly string[] BuiltOnDemand = ["step-functions", "workflows", "cloud-scheduler", "logic-apps", "event-grid", "aws-streams", "eventarc"];

    [Theory]
    [InlineData(DetectionKind.Runs, true)]
    [InlineData(DetectionKind.Records, true)]
    [InlineData(DetectionKind.Store, false)]
    [InlineData(DetectionKind.Signal, false)]
    [InlineData(DetectionKind.None, false)]
    public void Only_run_histories_and_failed_records_are_read_on_demand(DetectionKind kind, bool expected)
    {
        var row = ComponentCatalog.All.First() with { Detection = kind };

        ComponentAccessRules.ReadsOnDemand(row).Should().Be(expected);
    }

    [Fact]
    public void Reads_on_demand_refuses_a_missing_row()
    {
        ((Action)(() => ComponentAccessRules.ReadsOnDemand(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => ComponentAccessRules.Of(null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Every_component_read_on_demand_is_built_and_every_built_one_is_in_the_menu()
    {
        var rows = ComponentCatalog.All.Where(ComponentAccessRules.ReadsOnDemand).ToList();

        rows.Select(r => r.ComponentKey).Should().BeEquivalentTo(BuiltOnDemand);
        // Only a row with a recorded live run may claim Supported. Step Functions and Logic Apps have one (RECOVERY-EVIDENCE.md §7 and §8); add a key here only with its own.
        string[] withRecordedLiveRun = ["step-functions", "logic-apps"];
        rows.Where(r => !withRecordedLiveRun.Contains(r.ComponentKey))
            .Should().OnlyContain(r => r.Status == SupportStatus.BeingValidated, "no other row has a recorded live run yet, so none may claim Supported");
        rows.Where(r => withRecordedLiveRun.Contains(r.ComponentKey)).Should().OnlyContain(r => r.Status == SupportStatus.Supported && r.EvidenceRef != null);
        rows.Should().OnlyContain(r => r.InSidebar && r.Provider != null);
        rows.Should().OnlyContain(r => ComponentAccessRules.Of(r) == ComponentAccess.SameConnection);
    }

    [Fact]
    public void No_row_is_left_on_request_now_that_every_one_has_a_reader()
    {
        ComponentCatalog.All.Where(c => c.Status == SupportStatus.OnRequest).Should().BeEmpty();
    }

    [Theory]
    [InlineData(CloudKind.Azure, "service-bus", "storage-queues", "event-grid", "logic-apps")]
    [InlineData(CloudKind.Aws, "sqs", "sns", "lambda-eventbridge", "step-functions", "aws-streams")]
    [InlineData(CloudKind.Gcp, "pubsub", "eventarc", "workflows", "cloud-scheduler")]
    public void The_dialog_lists_every_component_of_a_cloud_once_in_catalogue_order(CloudKind cloud, params string[] keys)
    {
        ComponentAccessRules.Listed(cloud).Select(r => r.ComponentKey).Should().Equal(keys);
    }

    [Theory]
    [InlineData("event-grid", "Storage Blob Data Reader")]
    [InlineData("aws-streams", "sqs:ReceiveMessage")]
    [InlineData("eventarc", "roles/eventarc.viewer")]
    public void The_new_components_name_the_permission_their_check_tests(string key, string permission)
    {
        var setup = ComponentAccessRules.SetupFor(key)!;

        setup.Permissions.Should().Contain(permission);
        setup.Steps.Last().Should().Contain("Check access");
        setup.What.Should().NotBeNullOrWhiteSpace();
        setup.Appears.Should().Contain("Read only");
    }

    [Theory]
    [InlineData("logic-apps")]
    [InlineData("event-grid")]
    public void The_azure_components_tell_the_reader_which_connection_type_they_need(string key)
    {
        ComponentAccessRules.SetupFor(key)!.Steps.Should().Contain(s => s.Contains("Entra"), "a Service Bus connection string cannot read Resource Manager");
        ComponentAccessRules.SetupFor(key)!.Snippets.Should().NotBeEmpty().And.OnlyContain(s => s.Text.StartsWith("az ", StringComparison.Ordinal) || s.Text.StartsWith("curl ", StringComparison.Ordinal), "a command to copy: the Azure CLI, or the one call that creates the Entra connection");
    }

    [Fact]
    public void The_aws_streams_policy_is_valid_json_and_warns_about_the_redrive_policy()
    {
        var setup = ComponentAccessRules.SetupFor("aws-streams")!;

        var policy = System.Text.Json.JsonDocument.Parse(setup.Snippets.Single().Text);
        policy.RootElement.GetProperty("Statement").GetArrayLength().Should().Be(2);
        setup.Steps.Should().Contain(s => s.Contains("redrive", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_step_functions_policy_is_valid_json_too()
    {
        var setup = ComponentAccessRules.SetupFor("step-functions")!;
        var read = System.Text.Json.JsonDocument.Parse(setup.Snippets[0].Text);
        var redrive = System.Text.Json.JsonDocument.Parse(setup.Snippets[1].Text);

        setup.Snippets.Should().HaveCount(2);
        read.RootElement.GetProperty("Statement")[0].GetProperty("Action").GetArrayLength().Should().Be(3);
        redrive.RootElement.GetProperty("Statement")[0].GetProperty("Action")[0].GetString().Should().Be("states:RedriveExecution");
    }

    [Fact]
    public void The_logic_apps_guidance_says_a_resubmit_replays_every_step_and_names_the_role_that_allows_it()
    {
        var setup = ComponentAccessRules.SetupFor("logic-apps")!;

        setup.Permissions.Should().Contain("Reader");
        setup.Steps.Last().Should().Contain("Check access");
        setup.Appears.Should().Contain("resubmit").And.Contain("every step runs again").And.Contain("original run stays Failed").And.NotContain("Read only");
        setup.Needs.Should().Contain("Microsoft.Logic/workflows/triggers/histories/resubmit/action").And.Contain("Reader and Logic App Operator do not include")
            .And.Contain("Check access does not test it").And.NotContain("Nothing that runs, resubmits");
        setup.Snippets.Should().Contain(s => s.Text.Contains("role definition create", StringComparison.Ordinal) && s.Text.Contains("histories/resubmit/action", StringComparison.Ordinal));
        setup.Permissions.Should().NotContain(p => p.Contains("resubmit", StringComparison.OrdinalIgnoreCase), "the check tests only what reading needs");
    }

    [Fact]
    public void The_step_functions_guidance_says_a_person_can_redrive_and_which_permission_that_needs_and_that_check_access_skips_it()
    {
        var setup = ComponentAccessRules.SetupFor("step-functions")!;

        setup.Appears.Should().Contain("redrive").And.NotContain("Read only");
        setup.Needs.Should().Contain("states:RedriveExecution").And.Contain("Check access does not test");
        setup.Steps.Should().Contain(s => s.Contains("redrive", StringComparison.OrdinalIgnoreCase));
        setup.Permissions.Should().NotContain("states:RedriveExecution", "the check tests only what reading needs");
    }

    [Fact]
    public void A_key_with_no_written_setup_has_none_and_a_message_store_is_not_ticked()
    {
        ComponentAccessRules.SetupFor("no-such-key").Should().BeNull();
        ComponentCatalog.All.Where(c => c.Detection == DetectionKind.Store && c.Status == SupportStatus.Supported)
            .Should().OnlyContain(c => !ComponentAccessRules.ReadsOnDemand(c));
    }
}
