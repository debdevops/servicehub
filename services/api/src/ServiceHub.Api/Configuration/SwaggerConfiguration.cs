namespace ServiceHub.Api.Configuration;

/// <summary>
/// Configuration for OpenAPI documentation.
/// Supports both .NET 10 built-in OpenAPI (Scalar) and Swashbuckle (Swagger UI).
/// </summary>
public static class SwaggerConfiguration
{
    /// <summary>
    /// Adds OpenAPI and Swagger services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSwaggerConfiguration(this IServiceCollection services)
    {
        // .NET 10 built-in OpenAPI (serves JSON at /openapi/v1.json and Scalar UI at /scalar/v1)
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, context, ct) =>
            {
                document.Info.Title = "ServiceHub API";
                document.Info.Version = "v1";
                document.Info.Description = "Azure Service Bus forensic inspector and DLQ intelligence platform";
                return Task.CompletedTask;
            });
        });

        // Swashbuckle for Swagger UI (serves UI at /swagger and JSON at /swagger/v1/swagger.json)
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new()
            {
                Title = "ServiceHub API",
                Version = "v1",
                Description = "Azure Service Bus forensic inspector and DLQ intelligence platform"
            });
            
            // Include XML comments from the API assembly
            var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                c.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
            }
        });

        return services;
    }
}
