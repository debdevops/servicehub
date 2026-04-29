using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using ServiceHub.Api.Configuration;
using ServiceHub.Api.Filters;
using ServiceHub.Api.Middleware;
using ServiceHub.Api.Security;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Aws;
using ServiceHub.Infrastructure.Gcp;
using ServiceHub.Simulator;

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
    /// <param name="environment">The hosting environment; when <c>Simulator</c>, real cloud providers are replaced by in-memory fakes.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddServiceHubApi(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment? environment = null)
    {
        // Add infrastructure services (pass configuration for encryption key)
        services.AddInfrastructure(configuration);

        if (environment?.IsEnvironment("Simulator") == true)
        {
            // In Simulator mode all cloud providers are replaced by in-memory fakes.
            // No real Azure/AWS/GCP SDK calls are made.
            services.AddSimulatorProviders();
        }
        else
        {
            // Multi-cloud provider registration — feature-flagged, disabled by default in production.
            // Enabled in Development via appsettings.Development.json: CloudProviders:Aws:Enabled / :Gcp:Enabled
            if (configuration.GetValue<bool>("CloudProviders:Aws:Enabled"))
                services.AddAwsProvider();

            if (configuration.GetValue<bool>("CloudProviders:Gcp:Enabled"))
                services.AddGcpProvider();
        }

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

        // SPA token provider for co-hosted browser authentication
        services.AddSingleton<SpaTokenProvider>();

        // Security audit trail for critical operations
        services.AddSingleton<IAuditLogger, SecurityAuditLogger>();

        // Rate limit options (read from RateLimit config section)
        services.Configure<RateLimitOptions>(configuration.GetSection("RateLimit"));

        return services;
    }
}
