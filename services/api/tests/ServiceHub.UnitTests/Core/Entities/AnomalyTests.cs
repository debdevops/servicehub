using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.UnitTests.Core.Entities;

public sealed class AnomalyTests
{
    [Fact]
    public void Create_WithValidParameters_ShouldSucceed()
    {
        var namespaceId = Guid.NewGuid();
        var entityName = "test-queue";
        var type = AnomalyType.SlowProcessing;
        var severity = 75;
        var description = "High latency detected";

        var anomaly = Anomaly.Create(namespaceId, entityName, type, severity, description);

        anomaly.Id.Should().NotBeEmpty();
        anomaly.NamespaceId.Should().Be(namespaceId);
        anomaly.EntityName.Should().Be(entityName);
        anomaly.Type.Should().Be(type);
        anomaly.Severity.Should().Be(severity);
        anomaly.Description.Should().Be(description);
        anomaly.DetectedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Create_WithMetrics_ShouldStoreMetrics()
    {
        var metrics = new Dictionary<string, double>
        {
            ["latency_ms"] = 2500.5,
            ["message_count"] = 10000
        };

        var anomaly = Anomaly.Create(
            Guid.NewGuid(),
            "test-queue",
            AnomalyType.SlowProcessing,
            50,
            "Test",
            metrics);

        anomaly.Metrics.Should().BeEquivalentTo(metrics);
    }

    [Fact]
    public void Create_WithRecommendedActions_ShouldStoreActions()
    {
        var actions = new List<string>
        {
            "Scale up the service",
            "Check network configuration"
        };

        var anomaly = Anomaly.Create(
            Guid.NewGuid(),
            "test-queue",
            AnomalyType.SlowProcessing,
            50,
            "Test",
            recommendedActions: actions);

        anomaly.RecommendedActions.Should().BeEquivalentTo(actions);
    }

    [Fact]
    public void Create_WithoutMetricsOrActions_ShouldHaveEmptyCollections()
    {
        var anomaly = Anomaly.Create(
            Guid.NewGuid(),
            "test-queue",
            AnomalyType.SlowProcessing,
            50,
            "Test");

        anomaly.Metrics.Should().BeEmpty();
        anomaly.RecommendedActions.Should().BeEmpty();
    }

    [Fact]
    public void Create_WithNullEntityName_ShouldThrow()
    {
        var act = () => Anomaly.Create(
            Guid.NewGuid(),
            null!,
            AnomalyType.SlowProcessing,
            50,
            "Test");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_WithNullDescription_ShouldThrow()
    {
        var act = () => Anomaly.Create(
            Guid.NewGuid(),
            "test-queue",
            AnomalyType.SlowProcessing,
            50,
            null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(150, 100)]
    public void Create_WithVariousSeverityValues_ShouldClampTo0To100(int input, int expected)
    {
        var anomaly = Anomaly.Create(
            Guid.NewGuid(),
            "test-queue",
            AnomalyType.SlowProcessing,
            input,
            "Test");

        anomaly.Severity.Should().Be(expected);
    }

    [Theory]
    [InlineData(AnomalyType.HighMessageVolume)]
    [InlineData(AnomalyType.DeadLetterThresholdExceeded)]
    [InlineData(AnomalyType.QueueBacklog)]
    [InlineData(AnomalyType.HighFailureRate)]
    public void Create_WithDifferentAnomalyTypes_ShouldStoreCorrectType(AnomalyType type)
    {
        var anomaly = Anomaly.Create(
            Guid.NewGuid(),
            "test-queue",
            type,
            50,
            "Test");

        anomaly.Type.Should().Be(type);
    }
}
