using FluentAssertions;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.AI;

namespace ServiceHub.UnitTests.Infrastructure.AI;

public class ClusterExplanationRendererTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    private static ClusterExplanationRenderer.ClusterMetadata Cluster(
        int size = 10,
        int batchSize = 100,
        string? entity = "OrderService",
        string? reason = "MaxDeliveryCountExceeded",
        int? reasonCount = null,
        IReadOnlyList<string>? topTerms = null) =>
        new(size, batchSize, entity, reason, reasonCount ?? size, topTerms);

    private static SignatureLookupResult NewSignature(DateTimeOffset firstSeenAt) =>
        new(IsNew: true, FirstSeenAt: firstSeenAt, OccurrenceCount: 1);

    private static SignatureLookupResult RecurringSignature(DateTimeOffset firstSeenAt, int occurrenceCount) =>
        new(IsNew: false, FirstSeenAt: firstSeenAt, OccurrenceCount: occurrenceCount);

    // ── One case per deadletter reason, across all three providers ─────────

    [Theory]
    [InlineData("MaxDeliveryCountExceeded", "max delivery count exceeded")]
    [InlineData("TTLExpiredException", "message TTL expired")]
    [InlineData("HeaderSizeExceeded", "message header size limit exceeded")]
    [InlineData("MessageSizeExceeded", "message body size limit exceeded")]
    [InlineData("SessionFilterMismatch", "session filter mismatch")]
    public void Render_AzureReason_ProducesExpectedPhrase(string reason, string expectedPhrase)
    {
        var cluster = Cluster(reason: reason, topTerms: new[] { "should", "not", "appear" });

        var sentence = ClusterExplanationRenderer.Render(cluster, NewSignature(Now.AddMinutes(-18)), Now);

        sentence.Should().Contain(expectedPhrase);
        sentence.Should().NotContain("distinguishing terms");
    }

    [Fact]
    public void Render_AwsMaxReceiveCount_ProducesExpectedPhrase()
    {
        var cluster = Cluster(reason: "MaxReceiveCount");

        var sentence = ClusterExplanationRenderer.Render(cluster, NewSignature(Now.AddMinutes(-18)), Now);

        sentence.Should().Contain("SQS max receive count exceeded");
    }

    [Theory]
    [InlineData("maxDeliveryAttempts", "Pub/Sub max delivery attempts exceeded")]
    [InlineData("nack", "subscriber nack")]
    public void Render_GcpReason_ProducesExpectedPhrase(string reason, string expectedPhrase)
    {
        var cluster = Cluster(reason: reason);

        var sentence = ClusterExplanationRenderer.Render(cluster, NewSignature(Now.AddMinutes(-18)), Now);

        sentence.Should().Contain(expectedPhrase);
    }

    [Fact]
    public void Render_ExplicitAppReason_QuotesRawReasonAndShowsTerms()
    {
        var cluster = Cluster(reason: "OrderValidationFailed", topTerms: new[] { "sku", "invalid" });

        var sentence = ClusterExplanationRenderer.Render(cluster, NewSignature(Now.AddMinutes(-18)), Now);

        sentence.Should().Contain("application dead-letter: \"OrderValidationFailed\"");
        sentence.Should().Contain("distinguishing terms: sku, invalid");
    }

    [Fact]
    public void Render_UnknownReason_IsHonestAndShowsTerms()
    {
        var cluster = Cluster(reason: null, topTerms: new[] { "timeout" });

        var sentence = ClusterExplanationRenderer.Render(cluster, NewSignature(Now.AddMinutes(-18)), Now);

        sentence.Should().Contain("unknown failure reason");
        sentence.Should().Contain("distinguishing terms: timeout");
    }

    // ── Mixed-reason cluster ────────────────────────────────────────────────

    [Fact]
    public void Render_MixedReasons_ProducesHonestPhrasing()
    {
        var cluster = Cluster(size: 10, reason: "MaxDeliveryCountExceeded", reasonCount: 6, topTerms: new[] { "queue-a", "queue-b" });

        var sentence = ClusterExplanationRenderer.Render(cluster, NewSignature(Now.AddMinutes(-18)), Now);

        sentence.Should().Contain("mixed failure reasons");
        sentence.Should().NotContain("max delivery count exceeded");
        sentence.Should().Contain("distinguishing terms: queue-a, queue-b");
    }

    // ── Singleton / count phrasing ──────────────────────────────────────────

    [Fact]
    public void Render_SingleMessageCluster_UsesSingularNoun()
    {
        var cluster = Cluster(size: 1, batchSize: 50, reasonCount: 1);

        var sentence = ClusterExplanationRenderer.Render(cluster, null, Now);

        sentence.Should().StartWith("1 message");
        sentence.Should().NotContain("1 messages");
    }

    [Fact]
    public void Render_SingletonWithNoSignatureHistory_StillRendersCleanly()
    {
        var cluster = new ClusterExplanationRenderer.ClusterMetadata(
            Size: 1,
            BatchSize: 0,
            DominantEntity: "PaymentQueue",
            DominantDeadletterReason: "nack",
            DominantDeadletterReasonCount: 1,
            TopTerms: null);

        var sentence = ClusterExplanationRenderer.Render(cluster, null, Now);

        sentence.Should().Be("1 message: subscriber nack on PaymentQueue.");
    }

    // ── Missing entity ───────────────────────────────────────────────────────

    [Fact]
    public void Render_MissingEntity_StillReadsCorrectly()
    {
        var cluster = Cluster(entity: null);

        var sentence = ClusterExplanationRenderer.Render(cluster, NewSignature(Now.AddMinutes(-18)), Now);

        sentence.Should().NotContain(" on ,");
        sentence.Should().NotContain(" on .");
        sentence.Should().Be("10 messages (10% of the batch): max delivery count exceeded, first seen 18 minutes ago.");
    }

    // ── New vs recurring phrasing ────────────────────────────────────────────

    [Fact]
    public void Render_NewSignature_PhrasesFirstSeenRelatively()
    {
        var cluster = Cluster();

        var sentence = ClusterExplanationRenderer.Render(cluster, NewSignature(Now.AddMinutes(-18)), Now);

        sentence.Should().Contain("first seen 18 minutes ago");
        sentence.Should().NotContain("recurring");
    }

    [Fact]
    public void Render_RecurringSignature_PhrasesSpanAndOccurrenceCount()
    {
        var cluster = Cluster();

        var sentence = ClusterExplanationRenderer.Render(cluster, RecurringSignature(Now.AddDays(-9), occurrenceCount: 12), Now);

        sentence.Should().Contain("recurring, seen 12 times over 9 days");
    }

    [Fact]
    public void Render_NoSignatureHistory_OmitsTimeClauseWithoutTrailingPunctuation()
    {
        var cluster = Cluster();

        var sentence = ClusterExplanationRenderer.Render(cluster, null, Now);

        sentence.Should().NotContain(",.");
        sentence.Should().Be("10 messages (10% of the batch): max delivery count exceeded on OrderService.");
    }

    // ── Never empty or placeholder output ───────────────────────────────────

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("", "", false)]
    [InlineData("SomeAppReason", null, false)]
    [InlineData("nack", "Q", true)]
    public void Render_NoCombination_ProducesEmptyOrPlaceholderOutput(string? reason, string? entity, bool withSignature)
    {
        var cluster = Cluster(reason: reason, entity: entity, reasonCount: reason is null ? 0 : 10);
        var signature = withSignature ? NewSignature(Now.AddMinutes(-5)) : null;

        var sentence = ClusterExplanationRenderer.Render(cluster, signature, Now);

        sentence.Should().NotBeNullOrWhiteSpace();
        sentence.Should().NotContain("null");
        sentence.Should().NotContain("{");
        sentence.Should().NotContain("undefined");
    }

    [Fact]
    public void Render_ShareOfBatch_RoundsSubOnePercentInsteadOfShowingZero()
    {
        var cluster = Cluster(size: 1, batchSize: 10_000, reasonCount: 1);

        var sentence = ClusterExplanationRenderer.Render(cluster, null, Now);

        sentence.Should().Contain("<1% of the batch");
        sentence.Should().NotContain("0% of the batch");
    }

    [Fact]
    public void Render_WholeBatchInOneCluster_ShowsFullShare()
    {
        var cluster = Cluster(size: 25, batchSize: 25, reasonCount: 25);

        var sentence = ClusterExplanationRenderer.Render(cluster, null, Now);

        sentence.Should().Contain("100% of the batch");
    }
}
