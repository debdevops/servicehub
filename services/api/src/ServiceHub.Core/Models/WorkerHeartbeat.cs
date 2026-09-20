namespace ServiceHub.Core.Models;

/// <summary>
/// A single background worker's most recent successful-cycle timestamp, as recorded by
/// <see cref="Interfaces.IWorkerHeartbeatStore"/>.
/// </summary>
/// <param name="WorkerName">The reporting worker's class name (e.g. <c>"DlqMonitorWorker"</c>).</param>
/// <param name="LastHeartbeatAtUtc">When the worker last recorded a heartbeat.</param>
/// <param name="ExpectedInterval">
/// The worker's own configured cadence at the time of this heartbeat, used to judge staleness.
/// <see langword="null"/> for workers with no fixed cadence to judge against — event-driven
/// queue workers between jobs, and workers that are disabled by configuration — for which a
/// long gap since the last heartbeat is expected, not a sign of a stall.
/// </param>
public sealed record WorkerHeartbeat(string WorkerName, DateTimeOffset LastHeartbeatAtUtc, TimeSpan? ExpectedInterval);
