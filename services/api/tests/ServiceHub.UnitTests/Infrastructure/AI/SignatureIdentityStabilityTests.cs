using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.AI;
using ServiceHub.Shared.Helpers;

namespace ServiceHub.UnitTests.Infrastructure.AI;

/// <summary>
/// A failure signature's hash is its durable identity: operator knowledge, lifecycle status
/// (Active/Acknowledged/Resolved), incident history and the Home page's attention queue are all
/// keyed by it. If the hash moves while the underlying failure stays the same, all of that
/// silently detaches and the same failure is listed twice with its occurrence count split.
///
/// <para>
/// Found live on 2026-09-05 against the real AWS dev queue: the single failure
/// "Heuristic: generic error keywords" was recorded as <c>5e5177a9…</c> on 2026-08-30 and as
/// <c>d5c3e46a…</c> on 2026-09-05. The only difference between the two term sets was
/// <c>deliveryAttempts:10</c> vs <c>deliveryAttempts:4</c> — the cluster's raw mean delivery
/// count, which drifts continuously as messages enter and leave the DLQ.
/// <see cref="ClusterSignatureHasher"/> documents itself as excluding exactly this kind of
/// per-scan-varying input.
/// </para>
/// </summary>
public sealed class SignatureIdentityStabilityTests
{
    private static DlqMessage Message(int deliveryCount) => new()
    {
        MessageId = Guid.NewGuid().ToString(),
        SequenceNumber = 1,
        BodyHash = "abc",
        NamespaceId = Guid.Parse("83246adc-ce2a-4cbb-bae6-087a750a9dfa"),
        OwnerId = TestConstants.TestOwnerId,
        EntityName = "servicehub-dev-orders",
        EntityType = ServiceBusEntityType.Queue,
        EnqueuedTimeUtc = DateTimeOffset.UtcNow,
        DetectedAtUtc = DateTimeOffset.UtcNow,
        CloudProvider = CloudProviderType.Aws,
        DeadLetterReason = "MaxDeliveryCountExceeded",
        ForensicRootCause = "Heuristic: timeout-related keywords detected in error text.",
        DeliveryCount = deliveryCount,
    };

    private static async Task<IReadOnlyList<string>> TermsFor(params int[] deliveryCounts)
    {
        var strategy = new DeterministicClusteringStrategy();
        var result = await strategy.AnalyzeAsync(deliveryCounts.Select(Message).ToList());
        result.IsSuccess.Should().BeTrue();
        result.Value.Clusters.Should().NotBeEmpty(
            "the sample messages share an entity and reason so they must cluster");
        return result.Value.Clusters[0].TopTerms;
    }

    private static async Task<string> HashFor(params int[] deliveryCounts)
    {
        var strategy = new DeterministicClusteringStrategy();
        var result = await strategy.AnalyzeAsync(deliveryCounts.Select(Message).ToList());
        result.IsSuccess.Should().BeTrue();
        var cluster = result.Value.Clusters[0];
        return ClusterSignatureHasher.ComputeHash(cluster.TopTerms, cluster.DominantDeadletterReason);
    }

    [Fact]
    public async Task ClusterSignature_IsStable_WhenDeliveryCountsDriftWithinTheSameBand()
    {
        // Same real failure observed on two different days, when a different mix of messages
        // happened to be sitting in the DLQ. Delivery counts differ; the failure does not.
        var monday = await HashFor(4, 4, 5, 4);
        var friday = await HashFor(9, 10, 8, 10);

        friday.Should().Be(monday,
            "the same underlying failure must keep one identity — knowledge, lifecycle status and "
            + "incident history are keyed by this hash");
    }

    [Fact]
    public async Task ClusterSignature_DoesNotEmbedTheRawMeanDeliveryCount()
    {
        var terms = await TermsFor(4, 4);

        terms.Should().NotContain("deliveryAttempts:4",
            "the raw mean drifts every scan; only a coarse band is stable enough to hash");
    }

    [Fact]
    public async Task ClusterSignature_StillSeparates_GenuinelyDifferentRetryBehaviour()
    {
        // A failure that dies on its first attempt is not the same operational problem as one
        // that exhausts a long retry chain — bucketing must not collapse them together.
        var failsFast = await HashFor(1, 2, 1);
        var exhaustsRetries = await HashFor(40, 45, 50);

        exhaustsRetries.Should().NotBe(failsFast);
    }

    /// <summary>
    /// The trust/autonomy layer keys <c>AutonomyGrant</c> lookups on the fingerprint hash, so
    /// changing the fingerprint algorithm would silently invalidate every earned grant. This
    /// pins the exact hash for a fixed input: it must not change without a deliberate
    /// <see cref="FailureFingerprintBuilder"/> version bump.
    /// </summary>
    [Fact]
    public async Task FingerprintHash_ForAFixedFailure_IsUnchanged()
    {
        var features = new FailureFeatures
        {
            DeadLetterReason = "MaxDeliveryCountExceeded",
            EntityName = "orders",
            Provider = CloudProviderType.Azure,
            FailureCategory = "MaxDelivery",
            DeliveryCount = 10,
            ExceptionType = null,
        };

        var result = await new FailureFingerprintBuilder().ComputeAsync(features);

        result.IsSuccess.Should().BeTrue();
        result.Value.TopTerms.Should().Contain("deliveries:medium");
        result.Value.Hash.Should().Be(
            "aac7b240aa9569aacc7798817065e2f8a9dd24f176083c50f0ea869a19f64b05",
            "existing AutonomyGrants are keyed by this hash — this is the exact signature the "
            + "2026-09-03 soak run promoted to Standing (L4) against real Azure traffic");
    }
}
