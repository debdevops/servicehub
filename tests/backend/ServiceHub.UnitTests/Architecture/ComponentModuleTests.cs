using System.Text.RegularExpressions;
using FluentAssertions;
using ServiceHub.Core.Components;

namespace ServiceHub.UnitTests.Architecture;

/// <summary>
/// PLAN-PLUG-AND-PLAY-COMPONENTS §2 and §3.5, asserted over the source. A component module (an <c>IComponentSource</c>) reads and
/// nothing else, so adding one cannot add a way around the gate. Project references (a module depends on Core only) are held by
/// <see cref="DependencyDirectionTests"/>; these tests hold what that cannot see: what a module calls, and that it has a catalogue row.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class ComponentModuleTests
{
    private const string Src = "services/api/src";

    // A cloud SDK call that creates, changes, starts, stops or deletes something. A source may list, describe or get, and nothing else.
    private static readonly Regex ChangesACloud = new(
        @"\.(Start|Stop|Redrive|Send|Delete|Put|Create|Update|Tag|Untag|Publish|Purge|Reset|Retry|Cancel|Abort|Invoke|Set|Add|Remove|Write|Ack|Complete|Abandon|Resubmit|Replay)\w*Async\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // The services that change a cloud, or decide whether one may be changed. A module reaches none of them.
    private static readonly string[] Forbidden =
        ["IMessageOperationsService", "DlqReplayService", "RecoveryEligibilityGate", "IRecoveryLedger", "GetMessageSender", "IMessageSender"];

    private static IEnumerable<FileInfo> SourceFiles() =>
        new[] { "ServiceHub.Providers.Azure", "ServiceHub.Providers.Aws", "ServiceHub.Providers.Gcp" }
            .SelectMany(p => RepositoryRoot.CSharpFilesUnder($"{Src}/{p}"))
            // Everything in a module's Components folder is the module (its source and the seam it reads Google or AWS through), not only the
            // file that names IComponentSource.
            .Where(f => f.DirectoryName!.Replace('\\', '/').Contains("/Components", StringComparison.Ordinal));

    [Fact]
    public void There_is_at_least_one_source_to_check()
    {
        SourceFiles().Should().NotBeEmpty("an empty scan would let every rule below pass for the wrong reason");
    }

    [Fact]
    public void A_source_makes_no_call_that_changes_a_cloud()
    {
        var offenders = SourceFiles()
            .SelectMany(f => File.ReadLines(f.FullName).Select((line, i) => (f.Name, Line: i + 1, Text: line.TrimStart())))
            .Where(l => !l.Text.StartsWith("//", StringComparison.Ordinal) && ChangesACloud.IsMatch(l.Text))
            .Select(l => $"{l.Name}:{l.Line}: {l.Text}")
            .ToList();

        offenders.Should().BeEmpty("a component source only lists, describes and gets. Moving work on goes through DlqReplayService and the gate (rule 11)");
    }

    [Fact]
    public void A_source_cannot_reach_the_gate_the_ledger_or_a_message_sender()
    {
        var offenders = SourceFiles()
            .Where(f => Forbidden.Any(t => Regex.IsMatch(StripComments(File.ReadAllText(f.FullName)), $@"\b{t}\b")))
            .Select(f => f.Name)
            .ToList();

        offenders.Should().BeEmpty("a source that held a replay service, the gate or a sender would be a second door");
    }

    [Fact]
    public void Every_source_names_a_component_the_catalogue_knows()
    {
        foreach (var file in SourceFiles().Where(f => Regex.IsMatch(File.ReadAllText(f.FullName), @"class\s+\w+\s*:\s*[^{]*\bIComponentSource\b")))
        {
            var key = Regex.Match(File.ReadAllText(file.FullName), @"const\s+string\s+Key\s*=\s*""([^""]+)""");
            key.Success.Should().BeTrue($"{file.Name} must declare its catalogue key as `public const string Key`");
            ComponentCatalog.All.Should().Contain(c => c.ComponentKey == key.Groups[1].Value,
                $"{file.Name} serves '{key.Groups[1].Value}', which has no catalogue row");
        }
    }

    [Fact]
    public void The_component_read_endpoints_are_all_gets_and_call_only_the_source()
    {
        var text = File.ReadAllText(RepositoryRoot.CSharpFilesUnder($"{Src}/ServiceHub.Api").Single(f => f.Name == "NamespacesController.cs").FullName);
        var routes = Regex.Matches(text, @"\[(Http\w+)\(""\{id:guid\}/components/[^""]*""\)\]").Select(m => m.Groups[1].Value).ToList();

        routes.Should().NotBeEmpty();
        routes.Should().OnlyContain(r => r == "HttpGet", "a component read endpoint never changes anything");
    }

    private static string StripComments(string text) => Regex.Replace(text, @"//.*$", string.Empty, RegexOptions.Multiline);
}
