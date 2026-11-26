using FluentAssertions;
using ServiceHub.Core.Components;
using ServiceHub.UnitTests.Architecture;

namespace ServiceHub.UnitTests.Components;

/// <summary>
/// The README's coverage table is the catalogue's own Markdown (build-plan unit Z1), so the README cannot claim a component the code
/// does not list, or say Supported where the catalogue says it is still being validated. Run with <c>UPDATE_README=1</c> to rewrite it.
/// </summary>
public sealed class ReadmeCoverageTableTests
{
    private const string Start = "<!-- coverage-table:start -->";
    private const string End = "<!-- coverage-table:end -->";

    [Fact]
    public void The_readme_table_is_the_catalogue_table()
    {
        var path = Path.Combine(RepositoryRoot.Directory.FullName, "README.md");
        var readme = File.ReadAllText(path);
        var from = readme.IndexOf(Start, StringComparison.Ordinal);
        var to = readme.IndexOf(End, StringComparison.Ordinal);
        (from >= 0 && to > from).Should().BeTrue("the README must carry the two coverage-table markers");

        var expected = $"{Start}\n{ComponentCatalog.ToMarkdown().Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd()}\n{End}";
        var current = readme[from..(to + End.Length)].Replace("\r\n", "\n", StringComparison.Ordinal);

        if (Environment.GetEnvironmentVariable("UPDATE_README") == "1" && current != expected)
        {
            File.WriteAllText(path, readme[..from] + expected + readme[(to + End.Length)..]);
            return;
        }

        current.Should().Be(expected, "run the tests once with UPDATE_README=1 to regenerate the README table from ComponentCatalog.ToMarkdown()");
    }
}
