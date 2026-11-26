using System.Text.RegularExpressions;
using FluentAssertions;

namespace ServiceHub.UnitTests.Architecture;

/// <summary>
/// Rule R4, extended for 5.0.0: nothing outside <c>ServiceHub.Core</c> decides behaviour from a cloud's name. A cloud is a
/// heading on the screen; what may be done comes from the component catalogue and the adapter's capabilities.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class ComponentHonestyTests
{
    private static readonly Regex CloudNameBranch = new(@"\bCloudKind\.(Azure|Aws|Gcp)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Theory]
    [InlineData("services/api/src/ServiceHub.Infrastructure")]
    [InlineData("services/api/src/ServiceHub.Api")]
    [InlineData("services/api/src/ServiceHub.Providers.Azure")]
    [InlineData("services/api/src/ServiceHub.Providers.Aws")]
    [InlineData("services/api/src/ServiceHub.Providers.Gcp")]
    public void No_code_outside_core_branches_on_a_clouds_name(string relativePath)
    {
        var offenders = RepositoryRoot.CSharpFilesUnder(relativePath)
            .Where(file => File.ReadLines(file.FullName).Any(line =>
                !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && CloudNameBranch.IsMatch(line)))
            .Select(file => file.Name)
            .ToList();

        offenders.Should().BeEmpty("ask ComponentCatalog or ProviderCapabilities; a cloud's name decides nothing (R4)");
    }

    [Fact]
    public void The_recoverability_class_is_never_stored_or_set_by_hand()
    {
        // The class is derived from the facts. An entity or a DTO that carries a settable class could drift from them.
        var setter = new Regex(@"RecoverabilityClass\s+\w+\s*\{\s*get;\s*(set|init);", RegexOptions.Compiled);
        var offenders = RepositoryRoot.CSharpFilesUnder("services/api/src")
            .Where(file => setter.IsMatch(File.ReadAllText(file.FullName)))
            .Select(file => file.Name)
            .ToList();

        offenders.Should().BeEmpty("derive it with RecoverabilityFacts.Classify, or read ComponentDescriptor.Class");
    }
}
