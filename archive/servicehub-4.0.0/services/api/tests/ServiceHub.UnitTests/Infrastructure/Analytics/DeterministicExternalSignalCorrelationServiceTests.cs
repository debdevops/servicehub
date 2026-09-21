using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Analytics;

namespace ServiceHub.UnitTests.Infrastructure.Analytics;

public sealed class DeterministicExternalSignalCorrelationServiceTests
{
    private const string OwnerA = "entra:owner-a";
    private const string OwnerB = "entra:owner-b";

    private readonly DeterministicExternalSignalCorrelationService _sut = new();

    private static Anomaly CreateAnomaly(Guid namespaceId, string entityName = "queue-1") =>
        Anomaly.Create(namespaceId, entityName, AnomalyType.HighMessageVolume, 80, "spike");

    private static ExternalSignalEvent CreateSignal(
        string ownerId, DateTimeOffset occurredAt, Guid? namespaceId = null, ExternalSignalType type = ExternalSignalType.Deploy) => new()
    {
        OwnerId = ownerId,
        NamespaceId = namespaceId,
        SignalType = type,
        OccurredAt = occurredAt,
        Source = "manual",
        IngestedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void DetectCorrelations_SignalWithinWindow_ProducesCorrelation()
    {
        var namespaceId = Guid.NewGuid();
        var anomaly = CreateAnomaly(namespaceId);
        var signal = CreateSignal(OwnerA, anomaly.DetectedAt.AddMinutes(-30), namespaceId);
        var observations = new[] { new AnomalyObservation(anomaly, OwnerA, CloudProviderType.Azure) };

        var result = _sut.DetectCorrelations(observations, new[] { signal }, TimeSpan.FromHours(1));

        result.Should().HaveCount(1);
        result[0].SignalId.Should().Be(signal.Id);
        result[0].NamespaceId.Should().Be(namespaceId);
        result[0].EntityName.Should().Be(anomaly.EntityName);
    }

    [Fact]
    public void DetectCorrelations_SignalOutsideWindow_ProducesNoCorrelation()
    {
        var namespaceId = Guid.NewGuid();
        var anomaly = CreateAnomaly(namespaceId);
        var signal = CreateSignal(OwnerA, anomaly.DetectedAt.AddHours(-2), namespaceId);
        var observations = new[] { new AnomalyObservation(anomaly, OwnerA, CloudProviderType.Azure) };

        var result = _sut.DetectCorrelations(observations, new[] { signal }, TimeSpan.FromHours(1));

        result.Should().BeEmpty();
    }

    [Fact]
    public void DetectCorrelations_SignalAfterAnomaly_ProducesNoCorrelation()
    {
        var namespaceId = Guid.NewGuid();
        var anomaly = CreateAnomaly(namespaceId);
        var signal = CreateSignal(OwnerA, anomaly.DetectedAt.AddMinutes(30), namespaceId);
        var observations = new[] { new AnomalyObservation(anomaly, OwnerA, CloudProviderType.Azure) };

        var result = _sut.DetectCorrelations(observations, new[] { signal }, TimeSpan.FromHours(1));

        result.Should().BeEmpty();
    }

    [Fact]
    public void DetectCorrelations_FleetWideSignal_MatchesAnyNamespace()
    {
        var namespaceId = Guid.NewGuid();
        var anomaly = CreateAnomaly(namespaceId);
        var signal = CreateSignal(OwnerA, anomaly.DetectedAt.AddMinutes(-10), namespaceId: null);
        var observations = new[] { new AnomalyObservation(anomaly, OwnerA, CloudProviderType.Azure) };

        var result = _sut.DetectCorrelations(observations, new[] { signal }, TimeSpan.FromHours(1));

        result.Should().HaveCount(1);
        result[0].SignalId.Should().Be(signal.Id);
    }

    [Fact]
    public void DetectCorrelations_SignalScopedToDifferentNamespace_ProducesNoCorrelation()
    {
        var namespaceId = Guid.NewGuid();
        var otherNamespaceId = Guid.NewGuid();
        var anomaly = CreateAnomaly(namespaceId);
        var signal = CreateSignal(OwnerA, anomaly.DetectedAt.AddMinutes(-10), otherNamespaceId);
        var observations = new[] { new AnomalyObservation(anomaly, OwnerA, CloudProviderType.Azure) };

        var result = _sut.DetectCorrelations(observations, new[] { signal }, TimeSpan.FromHours(1));

        result.Should().BeEmpty();
    }

    [Fact]
    public void DetectCorrelations_DifferentOwnerSignal_ProducesNoCorrelation()
    {
        var namespaceId = Guid.NewGuid();
        var anomaly = CreateAnomaly(namespaceId);
        var signal = CreateSignal(OwnerB, anomaly.DetectedAt.AddMinutes(-10), namespaceId);
        var observations = new[] { new AnomalyObservation(anomaly, OwnerA, CloudProviderType.Azure) };

        var result = _sut.DetectCorrelations(observations, new[] { signal }, TimeSpan.FromHours(1));

        result.Should().BeEmpty();
    }

    [Fact]
    public void DetectCorrelations_MultipleSignalsInWindow_PicksNearest()
    {
        var namespaceId = Guid.NewGuid();
        var anomaly = CreateAnomaly(namespaceId);
        var farSignal = CreateSignal(OwnerA, anomaly.DetectedAt.AddMinutes(-50), namespaceId);
        var nearSignal = CreateSignal(OwnerA, anomaly.DetectedAt.AddMinutes(-5), namespaceId);
        var observations = new[] { new AnomalyObservation(anomaly, OwnerA, CloudProviderType.Azure) };

        var result = _sut.DetectCorrelations(observations, new[] { farSignal, nearSignal }, TimeSpan.FromHours(1));

        result.Should().HaveCount(1);
        result[0].SignalId.Should().Be(nearSignal.Id);
    }

    [Fact]
    public void DetectCorrelations_NoObservations_ReturnsEmpty()
    {
        var result = _sut.DetectCorrelations(Array.Empty<AnomalyObservation>(), Array.Empty<ExternalSignalEvent>(), TimeSpan.FromHours(1));

        result.Should().BeEmpty();
    }

    [Fact]
    public void DetectCorrelations_NullObservations_Throws()
    {
        var act = () => _sut.DetectCorrelations(null!, Array.Empty<ExternalSignalEvent>(), TimeSpan.FromHours(1));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DetectCorrelations_NullSignals_Throws()
    {
        var act = () => _sut.DetectCorrelations(Array.Empty<AnomalyObservation>(), null!, TimeSpan.FromHours(1));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DetectCorrelations_NonPositiveWindow_Throws()
    {
        var act = () => _sut.DetectCorrelations(Array.Empty<AnomalyObservation>(), Array.Empty<ExternalSignalEvent>(), TimeSpan.Zero);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
