namespace ServiceHub.Core.Events.Payloads;

/// <summary>
/// Payload for <see cref="EventTypes.AutoReplayRuleCircuitBreakerTripped"/>.
/// Raised when the success-rate circuit breaker automatically disables an auto-replay rule.
/// </summary>
public sealed record AutoReplayRuleCircuitBreakerTrippedPayload
{
    /// <summary>Identifier of the auto-replay rule that was disabled.</summary>
    public required long RuleId { get; init; }

    /// <summary>Display name of the disabled rule.</summary>
    public required string RuleName { get; init; }

    /// <summary>How many recent verified outcomes the trip was computed from.</summary>
    public required int SampleSize { get; init; }

    /// <summary>The verified success rate (Recovered / (Recovered + Returned)) that fell below
    /// the configured floor.</summary>
    public required double VerifiedSuccessRate { get; init; }

    /// <summary>UTC timestamp when the circuit breaker tripped.</summary>
    public required DateTimeOffset TrippedAtUtc { get; init; }
}
