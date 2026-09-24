using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServiceHub.Core.Interfaces;
using ServiceHub.Providers.Azure.ServiceBus;

namespace ServiceHub.Providers.Azure;

/// <summary>
/// The whole of what the rest of the product knows about Azure: one extension method, called from
/// <c>Program.cs</c>. Everything else reaches Azure through <see cref="ICloudMessagingProvider"/>.
/// </summary>
public static class AzureProviderServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Azure Service Bus provider, its client cache and factory, its sender and
    /// receiver, and its connectivity health check.
    /// </summary>
    public static IServiceCollection AddAzureProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Service Bus clients are expensive and thread-safe: one cache for the process.
        services.TryAddSingleton<IServiceBusClientCache, ServiceBusClientCache>();
        services.TryAddScoped<IServiceBusClientFactory, ServiceBusClientFactory>();
        services.TryAddScoped<IMessageSender, MessageSender>();
        services.TryAddScoped<IMessageReceiver, MessageReceiver>();

        // TryAddEnumerable: the router receives every registered provider, and a second call
        // must not register Azure twice.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICloudMessagingProvider, AzureMessagingProvider>());

        // Tagged "dependencies", not "ready": an unreachable Azure namespace is an external broker
        // outage, and must never flip /health/ready to Unhealthy and pull the instance out of
        // load-balancer rotation.
        services.AddHealthChecks()
            .AddCheck<ServiceBusHealthCheck>("servicebus", tags: ["dependencies", "servicebus"]);

        return services;
    }
}
