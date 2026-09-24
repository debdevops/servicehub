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
        app.MapHealthChecks("/health");

        // Readiness: the process can serve requests. Unit 1.1 registers the SQLite check here.
        app.MapHealthChecks("/health/ready");

        return app;
    }
}
