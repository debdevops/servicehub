using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Api.Extensions;

/// <summary>Health endpoints. Liveness and readiness are different questions.</summary>
public static class HealthEndpointExtensions
{
    /// <summary>Maps <c>/health</c> (liveness) and <c>/health/ready</c> (readiness).</summary>
    public static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Liveness: the process is up. Deliberately checks nothing else — a liveness probe that
        // fails because a cloud is unreachable restarts a container that was working fine.
        app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });

        // Readiness: the process can serve requests — today, that means its database answers.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(PersistenceServiceCollectionExtensions.ReadinessTag)
        });

        return app;
    }
}
