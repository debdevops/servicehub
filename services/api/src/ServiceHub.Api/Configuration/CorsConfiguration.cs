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

        // When true, development policy accepts requests from any hostname/IP —
        // required when the server is accessed over the network (not just localhost).
        // Never set this in Staging or Production configs.
        var allowAnyOriginInDev = corsSection.GetValue<bool>("DevelopmentAllowAnyOrigin");

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

            // SECURITY: Development policy.
            // When DevelopmentAllowAnyOrigin is true (set in appsettings.Development.json),
            // accept requests from any origin so users can reach the API from a remote host
            // (e.g. http://linuxhost:3000).  AllowCredentials() requires SetIsOriginAllowed
            // instead of AllowAnyOrigin() — both achieve the same result while keeping
            // credentials support.  This flag must never be true in Staging/Production.
            options.AddPolicy("DevelopmentPolicy", builder =>
            {
                if (allowAnyOriginInDev)
                {
                    builder.SetIsOriginAllowed(_ => true);
                }
                else
                {
                    // Restrict to explicit localhost origins when flag is off
                    var devOrigins = allowedOrigins.Length > 0
                        ? allowedOrigins
                        : new[]
                        {
                            "http://localhost:3000",
                            "http://localhost:5173",
                            "http://localhost:5174",
                            "http://127.0.0.1:3000",
                            "http://127.0.0.1:5173",
                            "http://127.0.0.1:5174",
                        };
                    builder.WithOrigins(devOrigins);
                }

                builder
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials()
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
        // SECURITY: Use explicit localhost origins even in development
        // Never use AllowAnyOrigin with credentials
        var policyName = environment.IsDevelopment() ? "DevelopmentPolicy" : PolicyName;
        app.UseCors(policyName);

        return app;
    }
}

