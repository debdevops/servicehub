using FluentAssertions;
using ServiceHub.Core.Components;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.UnitTests.Architecture;

namespace ServiceHub.UnitTests.Components;

/// <summary>The one coverage list stays honest: unique, evidence-backed, and never ahead of what the adapters can prove.</summary>
public sealed class ComponentCatalogTests
{
    [Fact]
    public void Every_row_has_a_unique_stable_id_in_component_dot_place_form()
    {
        ComponentCatalog.All.Select(c => c.Id).Should().OnlyHaveUniqueItems();
        ComponentCatalog.All.Should().OnlyContain(c => System.Text.RegularExpressions.Regex.IsMatch(c.Id, "^[a-z0-9-]+\\.[a-z0-9-]+$"));
    }

    [Fact]
    public void A_supported_row_names_evidence_that_exists_in_the_repository()
    {
        foreach (var row in ComponentCatalog.All.Where(c => c.Status == SupportStatus.Supported))
        {
            row.EvidenceRef.Should().NotBeNullOrWhiteSpace($"{row.Id} is Supported, and Supported means seen on a real cloud");
            File.Exists(Path.Combine(RepositoryRoot.Directory.FullName, row.EvidenceRef!)).Should().BeTrue($"{row.Id} points at {row.EvidenceRef}");
            row.Provider.Should().NotBeNull($"{row.Id} is Supported, so an adapter must read it");
        }
    }

    [Fact]
    public void The_menu_lists_only_components_that_are_supported_or_being_validated()
    {
        ComponentCatalog.All.Where(c => c.InSidebar)
            .Should().OnlyContain(c => c.Status == SupportStatus.Supported || c.Status == SupportStatus.BeingValidated);

        ComponentCatalog.Sidebar(CloudKind.Azure).Select(c => c.ComponentKey).Should().Equal("service-bus", "storage-queues", "event-grid", "logic-apps");
        ComponentCatalog.Sidebar(CloudKind.Aws).Select(c => c.ComponentKey).Should().Equal("sqs", "sns", "step-functions", "aws-streams");
        ComponentCatalog.Sidebar(CloudKind.Gcp).Select(c => c.ComponentKey).Should().Equal("pubsub", "eventarc", "workflows");
    }

    [Fact]
    public void Only_a_supported_row_can_offer_a_replay()
    {
        ComponentCatalog.All.Where(c => c.Status != SupportStatus.Supported)
            .Should().OnlyContain(c => !c.Class.OffersReplay() && !c.Class.OffersCloudRedrive(), "nothing is offered before a recorded live run");
    }

    [Fact]
    public void A_row_that_is_not_supported_says_why_or_which_unit_delivers_it()
    {
        ComponentCatalog.All.Where(c => c.Status == SupportStatus.NotSupported).Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Note));
        ComponentCatalog.All.Where(c => c.Status is SupportStatus.BeingValidated or SupportStatus.OnRequest)
            .Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.PlanUnit));
    }

    /// <summary>
    /// The catalogue may never claim more than the adapter declares. If a row says a replay can be proved, the adapter's own
    /// <see cref="ProviderCapabilities.CanProveDlqAbsence"/> must agree — and the other way round.
    /// </summary>
    [Fact]
    public void A_supported_row_claims_proof_exactly_when_its_adapter_can_prove_absence()
    {
        foreach (var row in ComponentCatalog.All.Where(c => c.Status == SupportStatus.Supported))
        {
            var canProve = ProviderCapabilities.For(row.Provider!.Value).CanProveDlqAbsence;
            row.Class.MayEarnAutonomy().Should().Be(canProve, $"{row.Id} and its adapter must agree about what can be proved");
        }
    }

    [Theory]
    [InlineData(CloudProviderType.Azure, "queue", "service-bus.dead-letter")]
    [InlineData(CloudProviderType.Azure, "subscription", "service-bus.dead-letter")]
    [InlineData(CloudProviderType.Aws, "queue", "sqs.dead-letter")]
    [InlineData(CloudProviderType.Aws, "subscription", "sns.subscriber-dead-letter")]
    [InlineData(CloudProviderType.Gcp, "subscription", "pubsub.dead-letter")]
    public void An_adapters_entity_maps_to_one_dead_letter_row(CloudProviderType provider, string kind, string expected) =>
        ComponentCatalog.ForEntity(provider, kind)!.Id.Should().Be(expected);

    [Fact]
    public void An_entity_the_catalogue_does_not_know_maps_to_nothing_never_to_a_guess()
    {
        ComponentCatalog.ForEntity(CloudProviderType.Gcp, "queue").Should().BeNull();
        ComponentCatalog.ForEntity(CloudProviderType.Azure, "exchange").Should().BeNull();
        ComponentCatalog.Find("no-such.row").Should().BeNull();
    }

    [Fact]
    public void Every_registered_adapter_belongs_to_a_cloud()
    {
        foreach (var provider in Enum.GetValues<CloudProviderType>())
        {
            ComponentCatalog.CloudOf(provider).Should().NotBeNull($"{provider} is stored on namespaces, so the menu must be able to place it");
        }
    }

    [Fact]
    public void The_api_row_carries_everything_derived_so_no_screen_derives_it_again()
    {
        var rows = CoverageResponse.Current.Rows;

        rows.Should().HaveCount(ComponentCatalog.All.Count);
        var serviceBus = rows.Single(r => r.Id == "service-bus.dead-letter");
        serviceBus.Should().Match<CoverageRow>(r => r.OffersReplay && r.MayEarnAutonomy && r.StatusWords == "Supported" && r.CloudName == "Azure");
        var sqs = rows.Single(r => r.Id == "sqs.dead-letter");
        sqs.Should().Match<CoverageRow>(r => r.OffersReplay && !r.MayEarnAutonomy && r.Class == RecoverabilityClass.R2ReplayableUnproven);
        rows.Single(r => r.Id == "storage-queues.poison").Should().Match<CoverageRow>(r => !r.OffersReplay && r.Class == RecoverabilityClass.R0Unknown);
    }

    [Fact]
    public void The_markdown_table_has_a_line_for_every_row()
    {
        var lines = ComponentCatalog.ToMarkdown().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.Should().HaveCount(ComponentCatalog.All.Count + 2, "a header, a rule, then one line per row");
        lines[2].Should().StartWith("| Azure | Service Bus | Dead-letter queue |");
    }

    [Theory]
    [InlineData("event-grid.dead-letter-blobs", CloudKind.Azure, DetectionKind.Records, CloudProviderType.Azure)]
    [InlineData("logic-apps.failed-runs", CloudKind.Azure, DetectionKind.Runs, CloudProviderType.Azure)]
    [InlineData("aws-streams.failure-records", CloudKind.Aws, DetectionKind.Records, CloudProviderType.Aws)]
    [InlineData("eventarc.undelivered", CloudKind.Gcp, DetectionKind.Records, CloudProviderType.Gcp)]
    public void A_component_that_was_on_request_is_now_read_only_and_being_validated(string id, CloudKind cloud, DetectionKind detection, CloudProviderType provider)
    {
        var row = ComponentCatalog.Find(id)!;

        row.Cloud.Should().Be(cloud);
        row.Detection.Should().Be(detection);
        row.Provider.Should().Be(provider);
        row.Status.Should().Be(SupportStatus.BeingValidated, "it has been built and tested against a fake cloud, not seen on a real one");
        row.InSidebar.Should().BeTrue();
        row.MovesOnBy.Should().Be("Handed to a person");
        row.Note.Should().Contain("Read only");
        row.EvidenceRef.Should().BeNull();
        row.Class.Should().Be(RecoverabilityClass.R0Unknown);
        row.Class.OffersReplay().Should().BeFalse();
        row.Class.OffersCloudRedrive().Should().BeFalse();
        row.Facts.Path.Should().Be(RecoveryPathKind.None, "moving work on is not built for it");
    }

    [Fact]
    public void A_failure_record_that_only_points_at_the_original_says_so()
    {
        var row = ComponentCatalog.Find("aws-streams.failure-records")!;

        row.Facts.Copy.Should().Be(CopyKind.Pointer);
        row.Note.Should().Contain("expires");
    }

    [Fact]
    public void The_markdown_table_says_how_the_new_rows_are_found_and_that_they_are_being_validated()
    {
        var markdown = ComponentCatalog.ToMarkdown();

        markdown.Should().Contain("Reads the failed records the service keeps");
        markdown.Split('\n').Where(l => l.Contains("| Event Grid |") || l.Contains("| Logic Apps |") || l.Contains("| Eventarc |") || l.Contains("Kinesis, DynamoDB streams"))
            .Should().HaveCount(4).And.OnlyContain(l => l.Contains("Being validated") && !l.Contains("On request"));
    }

    [Theory]
    [InlineData(DetectionKind.None, "—")]
    [InlineData(DetectionKind.Store, "Reads the place that holds the failed message")]
    [InlineData(DetectionKind.Runs, "Reads the history of failed runs")]
    [InlineData(DetectionKind.Records, "Reads the failed records the service keeps")]
    [InlineData(DetectionKind.Signal, "A metric or a state only")]
    public void Every_way_of_finding_something_has_plain_words(DetectionKind kind, string words)
    {
        ComponentCatalog.DetectionWords(kind).Should().Be(words);
    }

    [Theory]
    [InlineData(SupportStatus.Supported, "Supported")]
    [InlineData(SupportStatus.BeingValidated, "Being validated")]
    [InlineData(SupportStatus.OnRequest, "On request")]
    [InlineData(SupportStatus.NotSupported, "Not supported")]
    public void Every_status_has_plain_words(SupportStatus status, string words)
    {
        ComponentCatalog.StatusWords(status).Should().Be(words);
    }

    [Fact]
    public void The_sidebar_lists_one_entry_per_component_even_when_a_component_has_several_rows()
    {
        foreach (var cloud in Enum.GetValues<CloudKind>())
        {
            var keys = ComponentCatalog.Sidebar(cloud).Select(c => c.ComponentKey).ToList();
            keys.Should().OnlyHaveUniqueItems();
        }
    }
}
