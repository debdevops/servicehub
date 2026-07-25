using System.Text.Json;
using System.Text.Json.Serialization;
using ServiceHub.Api.Configuration;
using ServiceHub.Api.Filters;
using ServiceHub.Api.Middleware;
using ServiceHub.Api.Security;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Aws;
using ServiceHub.Infrastructure.Gcp;

namespace ServiceHub.Api.Extensions;

/// <summary>
/// Extension methods for configuring services in the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all ServiceHub API services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddServiceHubApi(this IServiceCollection services, IConfiguration configuration)
    {
        // Add infrastructure services (pass configuration for encryption key)
        services.AddInfrastructure(configuration);

        // Provider-specific forensic classification tiers — registered unconditionally
        // (independent of the CloudProviders:Aws/Gcp:Enabled live-provider flags) so both the
        // Simulator's seeded AWS/GCP DLQ messages and any real flag-enabled namespace get
        // provider-aware forensic analysis via IForensicEngineRouter. See
        // AwsDependencyInjection.AddAwsForensicIntelligence / GcpDependencyInjection.AddGcpForensicIntelligence.
        services.AddAwsForensicIntelligence();
        services.AddGcpForensicIntelligence();

        // Add API services
        services.AddApiServices(configuration);

        return services;
    }

    /// <summary>
    /// Adds API-specific services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Controllers with JSON options
        services.AddControllers(options =>
        {
            options.Filters.Add<ValidateModelAttribute>();
            options.Filters.Add<ApiExceptionFilterAttribute>();
            options.Filters.Add<ScopeAuthorizationFilter>(); // Enforce API key scopes
            options.SuppressAsyncSuffixInActionNames = true;
        })
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        });

        // Problem details
        services.AddProblemDetails();

        // HTTP headers configuration
        services.AddHttpHeadersConfiguration(configuration);

        // Security headers configuration
        services.AddSecurityHeadersConfiguration(configuration);

        // CORS
        services.AddCorsConfiguration(configuration);

        // Swagger
        services.AddSwaggerConfiguration();

        // Health checks
        services.AddHealthCheckConfiguration();

        // HTTP context accessor
        services.AddHttpContextAccessor();

        // Response caching
        services.AddResponseCaching();

        // Response compression
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
        });

        // Azure-specific cross-cloud trace searcher (extracted from CrossCloudTraceController)
        services.AddScoped<Services.IAzureTraceSearcher, Services.AzureTraceSearcher>();

        // SPA token provider for co-hosted browser authentication
        services.AddSingleton<SpaTokenProvider>();

        // Fan-out of platform events to connected SSE clients (GET /api/v1/events/stream)
        services.AddSingleton<Services.PlatformEventStreamBroker>();

        // Security audit trail for critical operations
        services.AddSingleton<IAuditLogger, SecurityAuditLogger>();

        // Throttles repeated API key authentication failures (see ApiKeyAuthenticationMiddleware).
        // Singleton so failure counts persist across requests for the process lifetime.
        services.AddSingleton<AuthFailureThrottle>();

        // Domain metrics (fleet backlog, etc.). AddMetrics guarantees IMeterFactory is available;
        // the meter is only exported when OpenTelemetry is enabled.
        services.AddMetrics();
        services.AddSingleton<Telemetry.ServiceHubMetrics>();

        // Strongly-typed, validated configuration (ServiceBus, RateLimit, Webhooks) with
        // fail-fast at startup. Also binds RateLimitOptions from the "RateLimit" section.
        services.AddServiceHubConfigurationValidation(configuration);

        return services;
    }
}
