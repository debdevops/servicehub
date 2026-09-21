using System.Diagnostics.Metrics;
using FluentAssertions;
using ServiceHub.Infrastructure.Telemetry;

namespace ServiceHub.UnitTests.Infrastructure.Telemetry;

/// <summary>
/// Verifies each <see cref="ServiceHubMetrics"/> instrument records with the expected tags, using
/// a real <see cref="Meter"/> (via <see cref="MeterListener"/>) rather than mocking — the
/// System.Diagnostics.Metrics API has no interface to mock against.
/// </summary>
public sealed class ServiceHubMetricsTests : IDisposable
{
    internal sealed class FakeMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }

    private readonly ServiceHubMetrics _sut;
    private readonly MeterListener _listener;
    private readonly List<(string Instrument, long Value, KeyValuePair<string, object?>[] Tags)> _recorded = [];

    public ServiceHubMetricsTests()
    {
        _sut = new ServiceHubMetrics(new FakeMeterFactory());

        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ServiceHubMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            _recorded.Add((instrument.Name, value, tags.ToArray())));
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
        _sut.Dispose();
    }

    [Fact]
    public void RecordEligibilityDecision_RecordsVerdictAndReason()
    {
        _sut.RecordEligibilityDecision("Escalate", "EMERGENCY_STOP_ACTIVE");

        var measurement = _recorded.Should().ContainSingle(
            m => m.Instrument == "servicehub.recovery.eligibility.decisions").Subject;
        measurement.Value.Should().Be(1);
        measurement.Tags.Should().Contain(new KeyValuePair<string, object?>("verdict", "Escalate"));
        measurement.Tags.Should().Contain(new KeyValuePair<string, object?>("reason", "EMERGENCY_STOP_ACTIVE"));
    }

    [Fact]
    public void RecordEligibilityDecision_NullReason_RecordsNone()
    {
        _sut.RecordEligibilityDecision("Allow", null);

        var measurement = _recorded.Should().ContainSingle(
            m => m.Instrument == "servicehub.recovery.eligibility.decisions").Subject;
        measurement.Tags.Should().Contain(new KeyValuePair<string, object?>("reason", "none"));
    }

    [Fact]
    public void RecordCircuitBreakerTrip_RecordsOne()
    {
        _sut.RecordCircuitBreakerTrip();

        _recorded.Should().ContainSingle(m => m.Instrument == "servicehub.recovery.circuitbreaker.trips")
            .Which.Value.Should().Be(1);
    }

    [Fact]
    public void RecordAutonomyTransition_RecordsDirectionAndLevels()
    {
        _sut.RecordAutonomyTransition("promotion", "Approve", "Standing");

        var measurement = _recorded.Should().ContainSingle(
            m => m.Instrument == "servicehub.recovery.autonomy.transitions").Subject;
        measurement.Tags.Should().Contain(new KeyValuePair<string, object?>("direction", "promotion"));
        measurement.Tags.Should().Contain(new KeyValuePair<string, object?>("from", "Approve"));
        measurement.Tags.Should().Contain(new KeyValuePair<string, object?>("to", "Standing"));
    }

    [Fact]
    public void RecordVerificationOutcome_RecordsOutcomeAndReason()
    {
        _sut.RecordVerificationOutcome("ObservationUnavailable", "AWS_NO_ABSENCE_PROOF");

        var measurement = _recorded.Should().ContainSingle(
            m => m.Instrument == "servicehub.recovery.verification.outcomes").Subject;
        measurement.Tags.Should().Contain(new KeyValuePair<string, object?>("outcome", "ObservationUnavailable"));
        measurement.Tags.Should().Contain(new KeyValuePair<string, object?>("reason", "AWS_NO_ABSENCE_PROOF"));
    }

    [Fact]
    public void RecordFleetOverview_RecordsRequestAndBacklog()
    {
        _sut.RecordFleetOverview(totalActive: 42, namespaceCount: 3);

        _recorded.Should().ContainSingle(m => m.Instrument == "servicehub.fleet.overview.requests")
            .Which.Value.Should().Be(1);
    }
}
