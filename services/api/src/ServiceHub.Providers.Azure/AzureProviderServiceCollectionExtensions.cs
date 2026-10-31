using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServiceHub.Core.Components;
using ServiceHub.Core.Interfaces;
using ServiceHub.Providers.Azure.Components;
using ServiceHub.Providers.Azure.MoveOn;
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

        // Components that are not message stores: read-only sources over Azure Resource Manager and Blob storage, one line each
        // (PLAN-PLUG-AND-PLAY-COMPONENTS). They sign in with the connection's own Entra identity and refuse a connection string.
        services.AddHttpClient("servicehub-azure-rest", c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
        services.TryAddSingleton<IAzureRestClient>(sp => new AzureRestClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("servicehub-azure-rest")));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IComponentSource, LogicAppsSource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IComponentSource, EventGridSource>());

        // Moving a failed Logic Apps run on (build-plan unit R2) goes through its own HTTP door, never the read-only one above. It has no
        // retry handler on purpose: Azure gives a resubmit no idempotency key, so a retried POST would start a second run.
        services.AddHttpClient("servicehub-azure-resubmit", c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
        services.TryAddSingleton<IAzureResubmitClient>(sp => new AzureResubmitClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("servicehub-azure-resubmit")));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IComponentMoveOn, LogicAppsMoveOn>());

        // Tagged "dependencies", not "ready": an unreachable Azure namespace is an external broker
        // outage, and must never flip /health/ready to Unhealthy and pull the instance out of
        // load-balancer rotation.
        services.AddHealthChecks()
            .AddCheck<ServiceBusHealthCheck>("servicebus", tags: ["dependencies", "servicebus"]);

        return services;
    }
}
