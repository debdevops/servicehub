using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Gcp;

/// <summary>
/// Extension methods for registering GCP Pub/Sub infrastructure services.
/// Call <see cref="AddGcpProvider"/> from the host application's DI setup,
/// gated behind the <c>CloudProviders:Gcp:Enabled</c> configuration flag.
/// </summary>
public static class GcpDependencyInjection
{
    /// <summary>
    /// Registers the GCP Pub/Sub messaging provider and all its dependencies.
    /// <para>
    /// Note: <see cref="GcpMessageReceiver"/> and <see cref="GcpMessageSender"/> are registered
    /// as concrete types (not as <c>IMessageReceiver</c>/<c>IMessageSender</c>) to avoid
    /// shadowing the Azure provider registration.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGcpProvider(this IServiceCollection services)
    {
        // GcpClientFactory is a singleton — GCP SDK clients are thread-safe and expensive to create.
        services.TryAddSingleton<IGcpClientFactory, GcpClientFactory>();

        // Register concrete types — NOT as IMessageReceiver/IMessageSender (those belong to Azure).
        services.AddScoped<GcpMessageReceiver>();
        services.AddScoped<GcpMessageSender>();

        // Register the provider as ICloudMessagingProvider (TryAddEnumerable prevents duplicates).
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<ICloudMessagingProvider, GcpMessagingProvider>());

        return services;
    }
}
