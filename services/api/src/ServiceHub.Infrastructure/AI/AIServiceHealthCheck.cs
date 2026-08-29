using Microsoft.Extensions.Diagnostics.HealthChecks;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.AI;

/// <summary>
/// Health check for the optional AI service dependency. Reports <see cref="HealthCheckResult.Degraded"/>
/// rather than Unhealthy when the AI service is unreachable — per <see cref="IAIServiceClient"/>'s own
/// contract, ServiceHub continues operating without AI-powered features (deterministic clustering
/// takes over), so an unavailable AI service is never a readiness failure. Calling
/// <see cref="IAIServiceClient.IsAvailableAsync"/> here is also what surfaces the client's own
/// "AI service is unavailable" warning (logged at most once per process) without requiring a real
/// AI-backed feature request to be exercised first.
/// </summary>
public sealed class AIServiceHealthCheck : IHealthCheck
{
    private readonly IAIServiceClient _aiServiceClient;

    /// <summary>Initializes a new instance of the <see cref="AIServiceHealthCheck"/> class.</summary>
    public AIServiceHealthCheck(IAIServiceClient aiServiceClient)
    {
        _aiServiceClient = aiServiceClient ?? throw new ArgumentNullException(nameof(aiServiceClient));
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await _aiServiceClient.IsAvailableAsync(cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return HealthCheckResult.Degraded(
                $"Could not determine AI service availability: {result.Error.Message}");
        }

        return result.Value
            ? HealthCheckResult.Healthy("AI service is available.")
            : HealthCheckResult.Degraded(
                "AI service is unavailable — ServiceHub continues operating with deterministic clustering only.");
    }
}
