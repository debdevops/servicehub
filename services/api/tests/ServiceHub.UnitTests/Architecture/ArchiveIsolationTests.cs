using System.Text.RegularExpressions;
using FluentAssertions;

namespace ServiceHub.UnitTests.Architecture;

/// <summary>
/// Rule R1's other half: the archive is a parts bin, not a dependency.
/// </summary>
/// <remarks>
/// The <c>Archive Freeze Guard</c> CI job stops anything being written <i>into</i> the archive.
/// This stops anything <i>reaching into</i> it. If new code ever imports from
/// <c>archive/servicehub-4.0.0/</c>, the freeze stops being a tidy-up and starts being load-bearing
/// for the shipping product — and 4.0.0 could then never be deleted.
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
    public void No_source_file_references_the_archive_outside_a_provenance_header(string relativePath)
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

                // A provenance header names where a file was copied FROM. That is a record, not a
                // reference — it compiles to nothing (ARCHITECTURE §8).
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                offenders.Add($"{file.Name} line {lineNumber}");
            }
        }

        offenders.Should().BeEmpty(
            "nothing outside archive/ may import from inside it (ADR-0013 D4). Copy what you need " +
            "out of the archive; never depend on it");
    }
}
