namespace ServiceHub.Simulator;

/// <summary>
/// Lightweight replaceable clock used throughout the simulator.
/// All simulated receivers read time through this clock so tests can advance time
/// deterministically to trigger visibility-window expiry and ack-deadline expiry.
/// </summary>
public sealed class SimulatorClock
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    /// <summary>Gets the current simulated UTC time.</summary>
    public DateTimeOffset UtcNow => _now;

    /// <summary>Advances the simulated clock by <paramref name="duration"/>.</summary>
    /// <param name="duration">The amount of time to advance.</param>
    public void Advance(TimeSpan duration) => _now = _now.Add(duration);

    /// <summary>Resets the simulated clock to the real wall-clock time.</summary>
    public void Reset() => _now = DateTimeOffset.UtcNow;
}
