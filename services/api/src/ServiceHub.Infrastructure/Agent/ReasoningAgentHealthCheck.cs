using Microsoft.Extensions.Diagnostics.HealthChecks;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Agent;

/// <summary>
/// Health check for the optional reasoning-companion dependency. Reports
/// <see cref="HealthCheckResult.Degraded"/> rather than Unhealthy when it is unreachable or
/// disabled — per <see cref="IReasoningAgentClient"/>'s own contract, ServiceHub operates
/// identically without it, so an unavailable companion is never a readiness failure. Mirrors
/// <see cref="AI.AIServiceHealthCheck"/>.
/// </summary>
public sealed class ReasoningAgentHealthCheck : IHealthCheck
{
    private readonly IReasoningAgentClient _reasoningAgentClient;

    /// <summary>Initializes a new instance of the <see cref="ReasoningAgentHealthCheck"/> class.</summary>
    public ReasoningAgentHealthCheck(IReasoningAgentClient reasoningAgentClient)
    {
        _reasoningAgentClient = reasoningAgentClient ?? throw new ArgumentNullException(nameof(reasoningAgentClient));
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await _reasoningAgentClient.IsAvailableAsync(cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return HealthCheckResult.Degraded(
                $"Could not determine reasoning companion availability: {result.Error.Message}");
        }

        return result.Value
            ? HealthCheckResult.Healthy("Reasoning companion is available.")
            : HealthCheckResult.Degraded(
                "Reasoning companion is disabled or unavailable — ServiceHub continues operating without it.");
    }
}
