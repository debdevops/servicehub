using System.Diagnostics.Metrics;

namespace ServiceHub.Api.Telemetry;

/// <summary>
/// Domain (business) metrics for ServiceHub operations, built on the Phase 1 OpenTelemetry
/// foundation. These complement the automatic ASP.NET/HTTP instrumentation with operations-
/// platform SLIs — e.g. how large the dead-letter backlog is across the fleet.
/// <para>
/// The underlying <see cref="Meter"/> always exists; it is only <i>exported</i> when
/// OpenTelemetry is enabled (see <c>ObservabilityExtensions</c>). When it is not, recording is
/// a cheap no-op, so this imposes no runtime cost on deployments that have not opted in.
/// </para>
/// </summary>
public sealed class ServiceHubMetrics : IDisposable
{
    /// <summary>The meter name to register with the OpenTelemetry metrics pipeline.</summary>
    public const string MeterName = "ServiceHub.Operations";

    private readonly Meter _meter;
    private readonly Counter<long> _fleetOverviewRequests;
    private readonly Histogram<int> _fleetActiveBacklog;

    /// <summary>Initializes a new instance of the <see cref="ServiceHubMetrics"/> class.</summary>
    public ServiceHubMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _meter = meterFactory.Create(MeterName);

        _fleetOverviewRequests = _meter.CreateCounter<long>(
            "servicehub.fleet.overview.requests",
            unit: "{request}",
            description: "Number of fleet overview requests served.");

        _fleetActiveBacklog = _meter.CreateHistogram<int>(
            "servicehub.fleet.active_backlog",
            unit: "{message}",
            description: "Active dead-letter backlog across the fleet at query time.");
    }

    /// <summary>Records that a fleet overview was produced, with its headline figures.</summary>
    /// <param name="totalActive">Total active dead-letters across the fleet.</param>
    /// <param name="namespaceCount">Number of namespaces in the fleet.</param>
    public void RecordFleetOverview(int totalActive, int namespaceCount)
    {
        _fleetOverviewRequests.Add(1);
        _fleetActiveBacklog.Record(
            totalActive,
            new KeyValuePair<string, object?>("namespace.count", namespaceCount));
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}
