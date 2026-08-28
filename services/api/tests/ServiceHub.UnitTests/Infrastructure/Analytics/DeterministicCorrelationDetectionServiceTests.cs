using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Analytics;

namespace ServiceHub.UnitTests.Infrastructure.Analytics;

public sealed class DeterministicCorrelationDetectionServiceTests
{
    private readonly DeterministicCorrelationDetectionService _sut = new();

    private static Anomaly CreateAnomaly(Guid namespaceId, string entityName, int severity = 70) =>
        Anomaly.Create(namespaceId, entityName, AnomalyType.HighMessageVolume, severity, "spike");

    [Fact]
    public void DetectCorrelations_NullArgument_Throws()
    {
        var act = () => _sut.DetectCorrelations(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DetectCorrelations_Empty_ReturnsEmpty()
    {
        var result = _sut.DetectCorrelations(Array.Empty<AnomalyObservation>());

        result.Should().BeEmpty();
    }

    [Fact]
    public void DetectCorrelations_SingleAnomaly_ProducesNoFinding()
    {
        var observations = new[]
        {
            new AnomalyObservation(CreateAnomaly(Guid.NewGuid(), "queue-1"), "key_owner1", CloudProviderType.Azure),
        };

        var result = _sut.DetectCorrelations(observations);

        result.Should().BeEmpty();
    }

    [Fact]
    public void DetectCorrelations_TwoEntitiesSameOwnerAndProvider_ProducesOneFindingWithBothMembers()
    {
        var namespaceA = Guid.NewGuid();
        var namespaceB = Guid.NewGuid();
        var observations = new[]
        {
            new AnomalyObservation(CreateAnomaly(namespaceA, "orders-queue", 80), "key_owner1", CloudProviderType.Azure),
            new AnomalyObservation(CreateAnomaly(namespaceB, "payments-queue", 60), "key_owner1", CloudProviderType.Azure),
        };

        var result = _sut.DetectCorrelations(observations);

        var finding = result.Should().ContainSingle().Subject;
        finding.OwnerId.Should().Be("key_owner1");
        finding.Provider.Should().Be(CloudProviderType.Azure);
        finding.Members.Should().HaveCount(2);
        finding.Members.Select(m => m.EntityName).Should().BeEquivalentTo("orders-queue", "payments-queue");
        finding.Severity.Should().Be(80); // max of member severities
    }

    [Fact]
    public void DetectCorrelations_DifferentOwners_DoNotCorrelateTogether()
    {
        var observations = new[]
        {
            new AnomalyObservation(CreateAnomaly(Guid.NewGuid(), "queue-1", 80), "key_owner1", CloudProviderType.Azure),
            new AnomalyObservation(CreateAnomaly(Guid.NewGuid(), "queue-2", 80), "key_owner2", CloudProviderType.Azure),
        };

        var result = _sut.DetectCorrelations(observations);

        // Each owner only contributed one entity to their own group — not enough for a
        // correlation, and critically the two owners' anomalies must never be merged together.
        result.Should().BeEmpty();
    }

    [Fact]
    public void DetectCorrelations_SameOwnerDifferentProviders_DoNotCorrelateTogether()
    {
        var observations = new[]
        {
            new AnomalyObservation(CreateAnomaly(Guid.NewGuid(), "queue-1", 80), "key_owner1", CloudProviderType.Azure),
            new AnomalyObservation(CreateAnomaly(Guid.NewGuid(), "queue-2", 80), "key_owner1", CloudProviderType.Aws),
        };

        var result = _sut.DetectCorrelations(observations);

        result.Should().BeEmpty();
    }

    [Fact]
    public void DetectCorrelations_ThreeEntitiesSameGroup_ProducesOneFindingWithAllThree()
    {
        var observations = new[]
        {
            new AnomalyObservation(CreateAnomaly(Guid.NewGuid(), "queue-1", 50), "key_owner1", CloudProviderType.Azure),
            new AnomalyObservation(CreateAnomaly(Guid.NewGuid(), "queue-2", 90), "key_owner1", CloudProviderType.Azure),
            new AnomalyObservation(CreateAnomaly(Guid.NewGuid(), "queue-3", 60), "key_owner1", CloudProviderType.Azure),
        };

        var result = _sut.DetectCorrelations(observations);

        var finding = result.Should().ContainSingle().Subject;
        finding.Members.Should().HaveCount(3);
        finding.Severity.Should().Be(90);
    }

    [Fact]
    public void DetectCorrelations_DuplicateEntityAcrossObservations_DeduplicatesByNamespaceAndEntity()
    {
        var namespaceA = Guid.NewGuid();
        var namespaceB = Guid.NewGuid();
        var anomalyA = CreateAnomaly(namespaceA, "queue-1", 80);

        var observations = new[]
        {
            new AnomalyObservation(anomalyA, "key_owner1", CloudProviderType.Azure),
            new AnomalyObservation(anomalyA, "key_owner1", CloudProviderType.Azure), // duplicate
            new AnomalyObservation(CreateAnomaly(namespaceB, "queue-2", 60), "key_owner1", CloudProviderType.Azure),
        };

        var result = _sut.DetectCorrelations(observations);

        var finding = result.Should().ContainSingle().Subject;
        finding.Members.Should().HaveCount(2);
    }
}
