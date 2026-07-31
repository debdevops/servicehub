using FluentAssertions;
using ServiceHub.Shared.Helpers;

namespace ServiceHub.UnitTests.Shared.Helpers;

public sealed class ClusterSignatureHasherTests
{
    [Fact]
    public void ComputeHash_SameMetadataAcrossTwoRuns_ProducesIdenticalHash()
    {
        var terms = new[] { "timeout", "connection", "sql" };
        const string reason = "MaxDeliveryCountExceeded";

        var hash1 = ClusterSignatureHasher.ComputeHash(terms, reason);
        var hash2 = ClusterSignatureHasher.ComputeHash(terms, reason);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeHash_TermOrderDiffers_ProducesIdenticalHash()
    {
        // Simulates the same underlying failure across two scans where near-tied TF-IDF
        // scores reordered the top-terms list without changing its membership.
        var hash1 = ClusterSignatureHasher.ComputeHash(
            new[] { "timeout", "connection", "sql" }, "MaxDeliveryCountExceeded");
        var hash2 = ClusterSignatureHasher.ComputeHash(
            new[] { "sql", "timeout", "connection" }, "MaxDeliveryCountExceeded");

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeHash_ClusterSizeChanges_HashUnaffectedWhenTermsAndReasonDoNot()
    {
        // ComputeHash takes no size/count/ref/timestamp input at all — this is the contract
        // that keeps the hash stable as message counts shift between scans of the same failure.
        var terms = new[] { "timeout", "connection", "sql" };
        const string reason = "MaxDeliveryCountExceeded";

        // Same terms/reason, standing in for a cluster of size 3 in one scan...
        var hashAtSizeThree = ClusterSignatureHasher.ComputeHash(terms, reason);
        // ...and size 30 in a later scan.
        var hashAtSizeThirty = ClusterSignatureHasher.ComputeHash(terms, reason);

        hashAtSizeThree.Should().Be(hashAtSizeThirty);
    }

    [Fact]
    public void ComputeHash_DifferentFailure_ProducesDifferentHash()
    {
        var hash1 = ClusterSignatureHasher.ComputeHash(
            new[] { "timeout", "connection", "sql" }, "MaxDeliveryCountExceeded");
        var hash2 = ClusterSignatureHasher.ComputeHash(
            new[] { "null", "reference", "deserialize" }, "UnhandledException");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeHash_DifferentDeadletterReasonOnly_ProducesDifferentHash()
    {
        var terms = new[] { "timeout", "connection", "sql" };

        var hash1 = ClusterSignatureHasher.ComputeHash(terms, "MaxDeliveryCountExceeded");
        var hash2 = ClusterSignatureHasher.ComputeHash(terms, "TTLExpiredException");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeHash_ReturnsSha256HexString()
    {
        var hash = ClusterSignatureHasher.ComputeHash(new[] { "timeout" }, "MaxDeliveryCountExceeded");

        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ComputeHash_NullTopTerms_Throws()
    {
        var act = () => ClusterSignatureHasher.ComputeHash(null!, "MaxDeliveryCountExceeded");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ComputeHash_NullDeadletterReason_Throws()
    {
        var act = () => ClusterSignatureHasher.ComputeHash(new[] { "timeout" }, null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
