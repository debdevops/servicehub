using System.Xml.Linq;
using FluentAssertions;

namespace ServiceHub.UnitTests.Architecture;

/// <summary>
/// ADR-0014 D5's dependency direction, asserted over the project files.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the difference between a convention and a rule.</b> A wrong
/// <c>ProjectReference</c> fails the build here instead of surviving a code review — and it is
/// what makes "the three clouds are peers" a compile-time fact rather than a habit.
/// </para>
/// <para>
/// It reads the <c>.csproj</c> files rather than the compiled assemblies on purpose. The compiler
/// drops a reference whose types are never used, so assembly metadata under-reports: a project
/// could carry a forbidden <c>ProjectReference</c> for months and only start failing the day
/// somebody used it. The declared reference is the thing the rule is about.
/// </para>
/// <para>
/// Gate 0 ② requires this to have been demonstrated failing: add a reference from
/// <c>ServiceHub.Providers.Azure</c> to <c>ServiceHub.Infrastructure</c>, watch it fail, revert.
/// </para>
/// </remarks>
[Trait("Category", "Architecture")]
public sealed class DependencyDirectionTests
{
    private const string Core = "ServiceHub.Core";
    private const string Infrastructure = "ServiceHub.Infrastructure";
    private const string Api = "ServiceHub.Api";

    private static readonly string[] Providers =
    [
        "ServiceHub.Providers.Azure",
        "ServiceHub.Providers.Aws",
        "ServiceHub.Providers.Gcp"
    ];

    [Fact]
    public void Core_depends_on_no_other_ServiceHub_project()
    {
        ProjectReferencesOf(Core).Should().BeEmpty(
            "ServiceHub.Core holds contracts and domain types and depends on nothing — not EF, not " +
            "a cloud SDK, not ASP.NET, and not another ServiceHub project (ADR-0014 D5)");
    }

    [Fact]
    public void Infrastructure_depends_on_Core_only()
    {
        ProjectReferencesOf(Infrastructure).Should().BeEquivalentTo([Core],
            "Infrastructure must never reference a provider project — ServiceHub.Api is the one " +
            "place the clouds and the infrastructure meet");
    }

    [Theory]
    [InlineData("ServiceHub.Providers.Azure")]
    [InlineData("ServiceHub.Providers.Aws")]
    [InlineData("ServiceHub.Providers.Gcp")]
    public void A_provider_depends_on_Core_only(string provider)
    {
        ProjectReferencesOf(provider).Should().BeEquivalentTo([Core],
            "each cloud adapter is a peer of the other two: it may depend on Core and its own SDK, " +
            "never on Infrastructure and never on another provider. A provider living inside " +
            "Infrastructure would give it reach the other two do not have");
    }

    [Fact]
    public void Api_is_the_only_project_that_sees_everything()
    {
        var references = ProjectReferencesOf(Api);

        references.Should().Contain(Core);
        references.Should().Contain(Infrastructure);
        references.Should().Contain(Providers, "the composition root registers every cloud");
    }

    [Fact]
    public void Every_source_project_is_covered_by_a_rule_above()
    {
        // Stops a seventh project appearing with no rule attached to it — the quiet way a
        // dependency rule stops describing the solution.
        var onDisk = new DirectoryInfo(Path.Combine(RepositoryRoot.Directory.FullName, "services/api/src"))
            .EnumerateDirectories()
            .Select(d => d.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        var covered = new[] { Core, Infrastructure, Api }.Concat(Providers).Order(StringComparer.Ordinal);

        onDisk.Should().BeEquivalentTo(covered,
            "a new project needs its own line in the dependency rules before it is built against");
    }

    private static IReadOnlyList<string> ProjectReferencesOf(string projectName)
    {
        var path = Path.Combine(
            RepositoryRoot.Directory.FullName, "services/api/src", projectName, $"{projectName}.csproj");

        File.Exists(path).Should().BeTrue($"'{projectName}' should exist at {path}");

        return [.. XDocument.Load(path)
            .Descendants("ProjectReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(include => include is not null)
            .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', Path.DirectorySeparatorChar)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }
}
