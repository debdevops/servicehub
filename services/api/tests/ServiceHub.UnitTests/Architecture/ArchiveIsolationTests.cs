using System.Text.RegularExpressions;
using FluentAssertions;

namespace ServiceHub.UnitTests.Architecture;

/// <summary>
/// Nothing in the product may depend on the frozen archive folder.
/// </summary>
/// <remarks>
/// The <c>Archive Freeze Guard</c> CI job stops anything being written <i>into</i> the archive.
/// This stops anything <i>reaching into</i> it, so the archive can be deleted without breaking a build.
/// </remarks>
[Trait("Category", "Architecture")]
public sealed class ArchiveIsolationTests
{
    private static readonly Regex ReachesIntoArchive = new(
        @"archive[/\\]servicehub-4\.0\.0",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Theory]
    [InlineData("services/api/src")]
    [InlineData("services/api/tests")]
    public void No_source_file_references_the_archive(string relativePath)
    {
        var offenders = new List<string>();

        foreach (var file in RepositoryRoot.CSharpFilesUnder(relativePath))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(file.FullName))
            {
                lineNumber++;

                if (!ReachesIntoArchive.IsMatch(line))
                {
                    continue;
                }

                offenders.Add($"{file.Name} line {lineNumber}");
            }
        }

        offenders.Should().BeEmpty(
            "nothing outside archive/ may import from inside it (ADR-0013 D4)");
    }
}
