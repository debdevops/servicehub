using System.Text.RegularExpressions;
using FluentAssertions;

namespace ServiceHub.UnitTests.Architecture;

/// <summary>
/// The Azure POST door (build-plan unit R2) is the only way ServiceHub sends anything but a read to Azure, so it is held to the same
/// standard as the read door: one place, one request, no body, and unreachable from a source.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class ResubmitDoorTests
{
    private const string Azure = "services/api/src/ServiceHub.Providers.Azure";

    private static string Code(FileInfo f) =>
        string.Join('\n', File.ReadLines(f.FullName).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static IEnumerable<FileInfo> AzureFiles() => RepositoryRoot.CSharpFilesUnder(Azure);

    [Fact]
    public void There_is_a_door_to_check()
    {
        AzureFiles().Should().Contain(f => f.Name == "AzureResubmitClient.cs");
    }

    [Fact]
    public void Nothing_in_the_azure_adapter_but_the_resubmit_door_sends_a_post_put_or_delete()
    {
        var offenders = AzureFiles()
            .Where(f => f.Name != "AzureResubmitClient.cs")
            .Where(f => Regex.IsMatch(Code(f), @"HttpMethod\.(Post|Put|Delete|Patch)|(?<!_resubmit)\.(PostAsync|PutAsync|DeleteAsync|PatchAsync|PostAsJsonAsync)\("))
            .Select(f => f.Name)
            .ToList();

        offenders.Should().BeEmpty("a second write door would let something move work on without the gate");
    }

    [Fact]
    public void The_resubmit_door_makes_exactly_one_post_request_with_no_body()
    {
        var text = Code(AzureFiles().Single(f => f.Name == "AzureResubmitClient.cs"));

        Regex.Matches(text, @"HttpMethod\.(\w+)").Select(m => m.Groups[1].Value).Should().OnlyContain(v => v == "Post");
        Regex.Matches(text, @"\bnew HttpRequestMessage\(").Should().ContainSingle("there is exactly one place a request is made");
        text.Should().NotContain("Content =", "a resubmit carries no body");
    }

    [Fact]
    public void The_resubmit_http_client_has_no_retry_handler()
    {
        var text = Code(AzureFiles().Single(f => f.Name == "AzureProviderServiceCollectionExtensions.cs"));
        var from = text.IndexOf("servicehub-azure-resubmit", StringComparison.Ordinal);
        from.Should().BeGreaterThan(-1);
        var block = text[from..Math.Min(text.Length, from + 600)];

        block.Should().NotMatchRegex("Resilience|Retry|Hedging|Polly", "Azure gives a resubmit no idempotency key, so a retried POST would start a second run");
    }

    [Fact]
    public void A_source_cannot_reach_the_resubmit_door()
    {
        var users = AzureFiles()
            .Where(f => f.DirectoryName!.Replace('\\', '/').Contains("/Components", StringComparison.Ordinal))
            .Where(f => Regex.IsMatch(Code(f), @"\bIAzureResubmitClient\b|\bAzureResubmitClient\b"))
            .Select(f => f.Name)
            .ToList();

        users.Should().BeEmpty("a component source only reads; moving work on goes through the gated redrive service");
    }
}
