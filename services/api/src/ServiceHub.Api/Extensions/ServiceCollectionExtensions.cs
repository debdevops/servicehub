using System.Text.Json;
using System.Text.Json.Serialization;
using ServiceHub.Api.Configuration;
using ServiceHub.Api.Filters;
using ServiceHub.Infrastructure;

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

        return services;
    }
}
