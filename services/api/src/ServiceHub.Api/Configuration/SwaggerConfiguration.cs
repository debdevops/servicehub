namespace ServiceHub.Api.Configuration;

/// <summary>
/// Configuration for OpenAPI documentation (.NET 10 built-in).
/// Replaces the previous Swashbuckle-based Swagger configuration.
/// </summary>
public static class SwaggerConfiguration
{
    /// <summary>
    /// Adds OpenAPI services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSwaggerConfiguration(this IServiceCollection services)
    {
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

        return services;
    }
}
