using FluentAssertions;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Recovery;

namespace ServiceHub.UnitTests.Recovery;

/// <summary>
/// "Needs your attention" shows a capped page of the oldest approvals. Dealt out one namespace at a time, that page can never be
/// one busy cloud's oldest N while another cloud's queue is missing altogether (found live 2026-10-03).
/// </summary>
public sealed class PendingWorkInterleaveTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);

    private static PendingWorkItem Approval(string provider, Guid ns, int minute) => new(
        "approval", Guid.NewGuid().ToString(), Guid.NewGuid(), null, minute, ns, provider + "-ns", provider, "dev", "orders", null, 1, "rule",
        "AUTONOMY_GRANT_INSUFFICIENT", "held", T0.AddMinutes(minute));

    [Fact]
    public void A_cloud_with_newer_approvals_is_still_in_the_first_page_when_another_cloud_has_many_older_ones()
    {
        var gcp = Guid.NewGuid();
        var aws = Guid.NewGuid();
        var oldestFirst = Enumerable.Range(0, 150).Select(i => Approval("gcp", gcp, i))
            .Concat(Enumerable.Range(200, 20).Select(i => Approval("aws", aws, i))).ToList();

        var firstPage = PendingWorkService.InterleaveByNamespace(oldestFirst).Take(100).ToList();

        firstPage.Count(i => i.Provider == "aws").Should().Be(20, "every AWS approval fits once the page is shared fairly");
        firstPage.Count(i => i.Provider == "gcp").Should().Be(80);
    }

    [Fact]
    public void Within_one_namespace_the_order_is_unchanged_and_nothing_is_lost_or_duplicated()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var input = new[] { Approval("gcp", a, 1), Approval("aws", b, 2), Approval("gcp", a, 3), Approval("aws", b, 4), Approval("gcp", a, 5) };

        var output = PendingWorkService.InterleaveByNamespace(input);

        output.Should().HaveCount(5).And.BeEquivalentTo(input);
        output.Where(i => i.NamespaceId == a).Select(i => i.Since).Should().BeInAscendingOrder();
        output.Where(i => i.NamespaceId == b).Select(i => i.Since).Should().BeInAscendingOrder();
        output[0].Since.Should().Be(input[0].Since, "the namespace waiting longest still leads");
    }

    [Fact]
    public void One_namespace_gets_exactly_the_order_it_always_had_and_nothing_gives_an_empty_list()
    {
        var a = Guid.NewGuid();
        var input = Enumerable.Range(0, 7).Select(i => Approval("aws", a, i)).ToList();

        PendingWorkService.InterleaveByNamespace(input).Should().Equal(input);
        PendingWorkService.InterleaveByNamespace([]).Should().BeEmpty();
    }
}
