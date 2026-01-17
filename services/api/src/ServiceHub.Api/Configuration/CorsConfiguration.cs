namespace ServiceHub.Api.Configuration;

/// <summary>
/// Configuration for Cross-Origin Resource Sharing (CORS).
/// </summary>
public static class CorsConfiguration
{
    /// <summary>
    /// The name of the CORS policy for the ServiceHub UI.
    /// </summary>
    public const string PolicyName = "ServiceHubPolicy";

    /// <summary>
    /// Adds CORS configuration to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCorsConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        var corsSection = configuration.GetSection("Cors");
        var allowedOrigins = corsSection.GetSection("AllowedOrigins").Get<string[]>() ?? [];

        // Get headers configuration for exposed headers
        var headersOptions = new HttpHeadersOptions();
        configuration.GetSection("HttpHeaders").Bind(headersOptions);

        services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, builder =>
            {
                if (allowedOrigins.Length > 0)
                {
                    builder.WithOrigins(allowedOrigins);
                }
                else
                {
                    // Fallback to development defaults if not configured
                    var devDefaults = corsSection.GetSection("DevelopmentDefaults").Get<string[]>() ?? [];
                    if (devDefaults.Length > 0)
                    {
                        builder.WithOrigins(devDefaults);
                    }
                }

                builder
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials()
                    .WithExposedHeaders(headersOptions.GetExposedHeaders());
            });

            // Permissive policy for development
            options.AddPolicy("AllowAll", builder =>
            {
                builder
                    .AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .WithExposedHeaders(headersOptions.GetExposedHeaders());
            });
        });

        return services;
    }

    /// <summary>
    /// Configures CORS middleware.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="environment">The host environment.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseCorsConfiguration(this IApplicationBuilder app, IHostEnvironment environment)
    {
        // Use permissive policy in development
        var policyName = environment.IsDevelopment() ? "AllowAll" : PolicyName;
        app.UseCors(policyName);

        return app;
    }
}

