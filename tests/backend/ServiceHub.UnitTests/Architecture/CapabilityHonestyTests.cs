using System.Text.RegularExpressions;
using FluentAssertions;

namespace ServiceHub.UnitTests.Architecture;

/// <summary>
/// Rule R4, enforced: outside the provider adapters, nothing branches on which cloud it is talking
/// to. It asks <c>ProviderCapabilities</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this test exists.</b> "Verified" may only be claimed where a queue's drain can actually
/// be proven, which is a per-namespace fact (<c>CanProveDlqAbsence</c>) and not a property of a
/// provider's name — the DLQ observer turns it true for AWS and GCP. A conditional on
/// <c>CloudProviderType.Azure</c> somewhere in the product is how "✓ Replayed on all three clouds"
/// gets shipped: simpler, prettier, and false on two of them.
/// </para>
/// <para>
/// The provider projects and the router are exempt — dispatching by provider is precisely their
/// job. <see cref="AllowedFiles"/> is the escape hatch: adding a file to it is a deliberate,
/// reviewed act with a reason written beside it, not a silent exception.
/// </para>
/// </remarks>
[Trait("Category", "Architecture")]
public sealed class CapabilityHonestyTests
{
    private static readonly Regex ProviderNameBranch = new(
        @"\bCloudProviderType\.(Azure|Aws|Gcp)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Files permitted to name a provider, each with the reason it is permitted.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedFiles = new(StringComparer.Ordinal)
    {
        // Core's single mapping from enum to capability preset — the one place the association is
        // allowed to exist, and the reason every other place can ask capabilities instead.
        ["ProviderCapabilities.cs"] = "the one enum-to-capabilities mapping (ServiceHub.Core)",

        // The router's entire job is dispatching by provider.
        ["CloudProviderRouter.cs"] = "dispatch by provider is this type's purpose",

        // Copied unedited from 4.0.0 (unit 2.6). Its one mention is the fail-closed default for a
        // request with no provider: `request.Provider ?? Aws` — AWS's capabilities, where absence
        // cannot be proven. It never branches on the name; it chooses the most cautious preset.
        ["RecoveryEligibilityGate.cs"] = "fail-closed default preset when a request names no provider (copied verbatim)"
    };

    [Theory]
    [InlineData("services/api/src/ServiceHub.Infrastructure")]
    [InlineData("services/api/src/ServiceHub.Api")]
    public void No_code_outside_a_provider_adapter_branches_on_the_provider_name(string relativePath)
    {
        var offenders = RepositoryRoot.CSharpFilesUnder(relativePath)
            .Where(file => !AllowedFiles.ContainsKey(file.Name))
            .Select(file => new { file, lines = MatchingLines(file) })
            .Where(x => x.lines.Count > 0)
            .Select(x => $"{x.file.Name}: {string.Join("; ", x.lines)}")
            .ToList();

        offenders.Should().BeEmpty(
            "rule R4 — capability differences stay explicit, and they are driven per namespace by " +
            "ProviderCapabilities (CanProveDlqAbsence above all), never by a provider's name. If a " +
            "genuine exception exists, add it to AllowedFiles with its reason");
    }

    private static List<string> MatchingLines(FileInfo file)
    {
        var results = new List<string>();
        var lineNumber = 0;

        foreach (var line in File.ReadLines(file.FullName))
        {
            lineNumber++;

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("///", StringComparison.Ordinal))
            {
                continue; // Prose may name a cloud; code may not.
            }

            if (ProviderNameBranch.IsMatch(line))
            {
                results.Add($"line {lineNumber}");
            }
        }

        return results;
    }
}
