using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.AI;

namespace ServiceHub.UnitTests.Infrastructure.AI;

public sealed class DeterministicClusteringStrategyTests
{
    private readonly DeterministicClusteringStrategy _sut = new();

    private static DlqMessage MakeMessage(
        string? reason = null,
        string? forensicRootCause = null,
        string entityName = "test-queue",
        CloudProviderType provider = CloudProviderType.Aws)
    {
        return new DlqMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            SequenceNumber = 1,
            BodyHash = "abc",
            NamespaceId = Guid.NewGuid(),
            OwnerId = TestConstants.TestOwnerId,
            EntityName = entityName,
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            CloudProvider = provider,
            DeadLetterReason = reason,
            ForensicRootCause = forensicRootCause,
        };
    }

    [Fact]
    public async Task Cluster_NoDeadLetterReason_FallsBackToForensicRootCause_NotUnknown()
    {
        // AWS SQS native redrive never sets DeadLetterReason, but the forensic engine
        // (SignalExtractor fix) now populates ForensicRootCause from application
        // properties — clustering should surface that instead of a bare "Unknown".
        var messages = new List<DlqMessage>
        {
            MakeMessage(reason: null, forensicRootCause: "Heuristic: timeout-related keywords detected in error text."),
            MakeMessage(reason: null, forensicRootCause: "Heuristic: timeout-related keywords detected in error text."),
        };

        var result = await _sut.AnalyzeAsync(messages);

        result.IsSuccess.Should().BeTrue();
        result.Value.Clusters.Should().ContainSingle();
        result.Value.Clusters[0].DominantDeadletterReason
            .Should().Be("Heuristic: timeout-related keywords detected in error text.");
        result.Value.Clusters[0].DominantDeadletterReason.Should().NotBe("Unknown");
        result.Value.Clusters[0].DominantDeadletterReasonCount.Should().Be(2);
    }

    [Fact]
    public async Task Cluster_NoReasonAndNoForensicRootCause_FallsBackToUnknown()
    {
        var messages = new List<DlqMessage>
        {
            MakeMessage(reason: null, forensicRootCause: null),
            MakeMessage(reason: null, forensicRootCause: null),
        };

        var result = await _sut.AnalyzeAsync(messages);

        result.Value.Clusters.Should().ContainSingle();
        result.Value.Clusters[0].DominantDeadletterReason.Should().Be("Unknown");
    }

    [Fact]
    public async Task Singleton_NoDeadLetterReason_FallsBackToForensicRootCause()
    {
        var messages = new List<DlqMessage>
        {
            MakeMessage(reason: null, forensicRootCause: "Heuristic: schema or validation keywords found.", entityName: "queue-a"),
        };

        var result = await _sut.AnalyzeAsync(messages);

        result.Value.Singletons.Should().ContainSingle();
        result.Value.Singletons[0].DominantDeadletterReason
            .Should().Be("Heuristic: schema or validation keywords found.");
    }

    [Fact]
    public async Task Cluster_AzureDeadLetterReasonPresent_UnaffectedByFallback()
    {
        // Regression guard: Azure messages that already have a native DeadLetterReason
        // must keep using it, never the ForensicRootCause fallback.
        var messages = new List<DlqMessage>
        {
            MakeMessage(reason: "MaxDeliveryCountExceeded", forensicRootCause: "Service Bus exceeded max delivery count.", provider: CloudProviderType.Azure),
            MakeMessage(reason: "MaxDeliveryCountExceeded", forensicRootCause: "Service Bus exceeded max delivery count.", provider: CloudProviderType.Azure),
        };

        var result = await _sut.AnalyzeAsync(messages);

        result.Value.Clusters[0].DominantDeadletterReason.Should().Be("MaxDeliveryCountExceeded");
    }
}
