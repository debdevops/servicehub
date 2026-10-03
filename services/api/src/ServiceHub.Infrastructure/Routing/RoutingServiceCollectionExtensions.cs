using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Messaging;

namespace ServiceHub.Infrastructure.Routing;

/// <summary>Registers the router that dispatches a namespace to its cloud's provider.</summary>
public static class RoutingServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="CloudProviderRouter"/> and the message-operations service built on it. The
    /// router receives every registered <see cref="ICloudMessagingProvider"/>; it never names one.
    /// </summary>
    public static IServiceCollection AddCloudProviderRouting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Scoped, not singleton: providers are scoped (they use the request's database context),
        // and a root-built singleton cannot resolve them under scope validation.
        services.TryAddScoped(sp => new CloudProviderRouter(sp.GetServices<ICloudMessagingProvider>()));
        services.TryAddScoped<ICloudProviderRouter>(sp => sp.GetRequiredService<CloudProviderRouter>());
        services.TryAddScoped<IMessageOperationsService, MessageOperationsService>();

        return services;
    }
}
