using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServiceHub.Core.Interfaces;
using ServiceHub.Simulator.Providers.Aws;
using ServiceHub.Simulator.Providers.Azure;
using ServiceHub.Simulator.Providers.Gcp;
using ServiceHub.Simulator.Store;

namespace ServiceHub.Simulator;

/// <summary>
/// Dependency injection extensions for the in-memory simulator.
/// Call <see cref="AddSimulatorProviders"/> instead of any real provider registration
/// when <c>ASPNETCORE_ENVIRONMENT=Simulator</c>.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers all simulator services as <b>Singleton</b>:
    /// <list type="bullet">
    ///   <item><see cref="ISimulatorStore"/> → <see cref="InMemorySimulatorStore"/></item>
    ///   <item><see cref="SimulatorClock"/></item>
    ///   <item><see cref="SimulatorDataSeeder"/></item>
    ///   <item>All three simulated messaging providers (Azure, AWS, GCP)</item>
    ///   <item><see cref="IMessageReceiver"/> → <see cref="SimulatorMessageReceiver"/> (replaces real impl)</item>
    ///   <item><see cref="IMessageSender"/> → <see cref="SimulatorMessageSender"/> (replaces real impl)</item>
    /// </list>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSimulatorProviders(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Core simulator infrastructure
        services.AddSingleton<ISimulatorStore, InMemorySimulatorStore>();
        services.AddSingleton<SimulatorClock>();
        services.AddSingleton<SimulatorDataSeeder>();

        // Azure simulated provider
        services.AddSingleton<SimulatedAzureReceiver>();
        services.AddSingleton<SimulatedAzureSender>();
        services.AddSingleton<SimulatedAzureMessagingProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICloudMessagingProvider, SimulatedAzureMessagingProvider>());

        // AWS simulated provider
        services.AddSingleton<SimulatedAwsReceiver>();
        services.AddSingleton<SimulatedAwsSender>();
        services.AddSingleton<SimulatedAwsMessagingProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICloudMessagingProvider, SimulatedAwsMessagingProvider>());

        // GCP simulated provider
        services.AddSingleton<SimulatedGcpReceiver>();
        services.AddSingleton<SimulatedGcpSender>();
        services.AddSingleton<SimulatedGcpMessagingProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICloudMessagingProvider, SimulatedGcpMessagingProvider>());

        // Replace the real IMessageReceiver / IMessageSender with routing singletons that
        // delegate to the correct simulated provider based on namespace ID.
        services.Replace(ServiceDescriptor.Singleton<IMessageReceiver, SimulatorMessageReceiver>());
        services.Replace(ServiceDescriptor.Singleton<IMessageSender, SimulatorMessageSender>());

        // Hosted service to seed data at startup
        services.AddHostedService<SimulatorSeedHostedService>();

        return services;
    }
}
