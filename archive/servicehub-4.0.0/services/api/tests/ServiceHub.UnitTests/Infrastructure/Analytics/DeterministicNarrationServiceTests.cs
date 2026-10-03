using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Analytics;

namespace ServiceHub.UnitTests.Infrastructure.Analytics;

public sealed class DeterministicNarrationServiceTests
{
    private readonly DeterministicNarrationService _sut = new();

    private static Namespace CreateNamespace(string name = "orders-ns") =>
        Namespace.Create(
            name,
            $"Endpoint=sb://{name}.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Test NS").Value;

    [Fact]
    public void GenerateNarrations_NoFindings_ReturnsEmpty()
    {
        var result = _sut.GenerateNarrations(
            new Dictionary<Guid, Namespace>(),
            Array.Empty<Anomaly>(),
            Array.Empty<DriftFinding>(),
            Array.Empty<CorrelationFinding>());

        result.Should().BeEmpty();
    }

    [Fact]
    public void GenerateNarrations_AnomaliesInOneNamespace_ProducesOneNamespaceActivityNarration()
    {
        var ns = CreateNamespace();
        var anomaly = Anomaly.Create(ns.Id, "orders-queue", AnomalyType.HighMessageVolume, 85, "spike");

        var result = _sut.GenerateNarrations(
            new Dictionary<Guid, Namespace> { [ns.Id] = ns },
            [anomaly],
            Array.Empty<DriftFinding>(),
            Array.Empty<CorrelationFinding>());

        result.Should().HaveCount(1);
        var narration = result[0];
        narration.Kind.Should().Be(NarrationKind.NamespaceActivity);
        narration.NamespaceId.Should().Be(ns.Id);
        narration.AccessNamespaceIds.Should().ContainSingle().Which.Should().Be(ns.Id);
        narration.Severity.Should().Be(85);
        narration.ContributingAnomalyIds.Should().ContainSingle().Which.Should().Be(anomaly.Id);
        narration.Headline.Should().Contain(ns.Name);
        narration.Summary.Should().Contain(ns.Name);
    }

    [Fact]
    public void GenerateNarrations_AnomalyAndDriftInSameNamespace_CombinesIntoOneNarration()
    {
        var ns = CreateNamespace();
        var anomaly = Anomaly.Create(ns.Id, "orders-queue", AnomalyType.HighMessageVolume, 60, "spike");
        var drift = DriftFinding.Create(ns.Id, "orders-queue", DriftFindingType.SchemaShapeDrift, 90, "shape changed");

        var result = _sut.GenerateNarrations(
            new Dictionary<Guid, Namespace> { [ns.Id] = ns },
            [anomaly],
            [drift],
            Array.Empty<CorrelationFinding>());

        result.Should().HaveCount(1);
        var narration = result[0];
        narration.Severity.Should().Be(90, "the narration reports the max severity across contributing findings");
        narration.ContributingAnomalyIds.Should().Contain(anomaly.Id);
        narration.ContributingDriftFindingIds.Should().Contain(drift.Id);
    }

    [Fact]
    public void GenerateNarrations_FindingsInTwoNamespaces_ProducesTwoNarrations()
    {
        var nsA = CreateNamespace("ns-a");
        var nsB = CreateNamespace("ns-b");
        var anomalyA = Anomaly.Create(nsA.Id, "queue-a", AnomalyType.HighMessageVolume, 50, "spike a");
        var anomalyB = Anomaly.Create(nsB.Id, "queue-b", AnomalyType.HighMessageVolume, 50, "spike b");

        var result = _sut.GenerateNarrations(
            new Dictionary<Guid, Namespace> { [nsA.Id] = nsA, [nsB.Id] = nsB },
            [anomalyA, anomalyB],
            Array.Empty<DriftFinding>(),
            Array.Empty<CorrelationFinding>());

        result.Should().HaveCount(2);
        result.Select(n => n.NamespaceId).Should().BeEquivalentTo([nsA.Id, nsB.Id]);
    }

    [Fact]
    public void GenerateNarrations_CorrelationFinding_ProducesCrossNamespaceNarration()
    {
        var nsA = CreateNamespace("ns-a");
        var nsB = CreateNamespace("ns-b");

        var correlation = CorrelationFinding.Create(
            "owner-1",
            [
                new CorrelationMember(nsA.Id, "queue-a", AnomalyType.HighMessageVolume, 70, CloudProviderType.Azure),
                new CorrelationMember(nsB.Id, "queue-b", AnomalyType.HighMessageVolume, 80, CloudProviderType.Azure),
            ],
            80,
            "correlated spike");

        var result = _sut.GenerateNarrations(
            new Dictionary<Guid, Namespace> { [nsA.Id] = nsA, [nsB.Id] = nsB },
            Array.Empty<Anomaly>(),
            Array.Empty<DriftFinding>(),
            [correlation]);

        result.Should().HaveCount(1);
        var narration = result[0];
        narration.Kind.Should().Be(NarrationKind.CrossNamespaceCorrelation);
        narration.NamespaceId.Should().BeNull();
        narration.AccessNamespaceIds.Should().BeEquivalentTo([nsA.Id, nsB.Id]);
        narration.Severity.Should().Be(80);
        narration.ContributingCorrelationFindingIds.Should().ContainSingle().Which.Should().Be(correlation.Id);
    }

    [Fact]
    public void GenerateNarrations_CrossProviderCorrelationFinding_HeadlineMentionsBothProviders()
    {
        var nsA = CreateNamespace("ns-a");
        var nsB = CreateNamespace("ns-b");

        var correlation = CorrelationFinding.Create(
            "owner-1",
            [
                new CorrelationMember(nsA.Id, "queue-a", AnomalyType.HighMessageVolume, 70, CloudProviderType.Azure),
                new CorrelationMember(nsB.Id, "queue-b", AnomalyType.HighMessageVolume, 80, CloudProviderType.Aws),
            ],
            80,
            "cross-cloud correlated spike");

        var result = _sut.GenerateNarrations(
            new Dictionary<Guid, Namespace> { [nsA.Id] = nsA, [nsB.Id] = nsB },
            Array.Empty<Anomaly>(),
            Array.Empty<DriftFinding>(),
            [correlation]);

        result.Should().HaveCount(1);
        result[0].Headline.Should().Contain("Azure").And.Contain("Aws");
    }

    [Fact]
    public void GenerateNarrations_NamespaceMissingFromLookup_FallsBackToIdInText()
    {
        var namespaceId = Guid.NewGuid();
        var anomaly = Anomaly.Create(namespaceId, "some-queue", AnomalyType.HighMessageVolume, 55, "spike");

        var result = _sut.GenerateNarrations(
            new Dictionary<Guid, Namespace>(),
            [anomaly],
            Array.Empty<DriftFinding>(),
            Array.Empty<CorrelationFinding>());

        result.Should().HaveCount(1);
        result[0].Headline.Should().Contain(namespaceId.ToString());
    }

    [Fact]
    public void GenerateNarrations_NullArguments_Throw()
    {
        var emptyLookup = new Dictionary<Guid, Namespace>();

        ((Action)(() => _sut.GenerateNarrations(null!, [], [], []))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _sut.GenerateNarrations(emptyLookup, null!, [], []))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _sut.GenerateNarrations(emptyLookup, [], null!, []))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _sut.GenerateNarrations(emptyLookup, [], [], null!))).Should().Throw<ArgumentNullException>();
    }
}
