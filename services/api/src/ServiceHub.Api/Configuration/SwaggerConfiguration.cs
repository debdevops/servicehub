using System.Reflection;
using Microsoft.OpenApi.Models;
using ServiceHub.Shared.Helpers;

namespace ServiceHub.Api.Configuration;

/// <summary>
/// Configuration for Swagger/OpenAPI documentation.
/// </summary>
public static class SwaggerConfiguration
{
    /// <summary>
    /// Adds Swagger services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSwaggerConfiguration(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "ServiceHub API",
                Version = "v1",
                Description = "Azure Service Bus Inspector API. " +
                              "Provides endpoints for managing Service Bus namespaces, " +
                              "sending messages, peeking queues/topics, and monitoring dead-letter queues.",
                Contact = new OpenApiContact
                {
                    Name = "ServiceHub Team",
                    Email = "servicehub@example.com"
                },
                License = new OpenApiLicense
                {
                    Name = "MIT",
                    Url = new Uri("https://opensource.org/licenses/MIT")
                }
            });

            // Add correlation ID header parameter
            options.AddSecurityDefinition("CorrelationId", new OpenApiSecurityScheme
            {
                Description = "Correlation ID for distributed tracing. If not provided, one will be generated.",
                Name = CorrelationIdGenerator.DefaultHeaderName,
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "correlationid"
            });

            // Add XML comments
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }

            // Group endpoints by tag
            options.TagActionsBy(api =>
            {
                if (api.GroupName is not null)
                {
                    return [api.GroupName];
                }

                if (api.ActionDescriptor is Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor controllerActionDescriptor)
                {
                    return [controllerActionDescriptor.ControllerName];
                }

                return ["Other"];
            });

            options.DocInclusionPredicate((_, _) => true);

            // Order tags
            options.OrderActionsBy(api => api.RelativePath);

            // Custom schema IDs
            options.CustomSchemaIds(type => type.FullName?.Replace('+', '.'));
        });

        return services;
    }

    /// <summary>
    /// Configures Swagger middleware.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseSwaggerConfiguration(this IApplicationBuilder app)
    {
        app.UseSwagger(options =>
        {
            options.RouteTemplate = "swagger/{documentName}/swagger.json";
        });

        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "ServiceHub API v1");
            options.RoutePrefix = "swagger";
            options.DocumentTitle = "ServiceHub API Documentation";
            options.DisplayRequestDuration();
            options.EnableDeepLinking();
            options.EnableFilter();
            options.ShowExtensions();
            options.EnableTryItOutByDefault();
        });

        return app;
    }
}
