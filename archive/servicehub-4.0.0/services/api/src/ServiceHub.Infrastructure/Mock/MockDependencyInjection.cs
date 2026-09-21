using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Mock;

/// <summary>
/// Dependency injection extensions for the mock/in-memory messaging provider.
/// </summary>
public static class MockDependencyInjection
{
    /// <summary>
    /// Registers the <see cref="InMemoryMessagingProvider"/> and its supporting services.
    /// The mock provider is automatically activated when the
    /// <c>SERVICEHUB_MOCK_PROVIDER=true</c> environment variable is set or when a
    /// namespace connection string starts with <c>mock://</c>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMockProviders(this IServiceCollection services)
    {
        // Singleton store — seeded once per process lifetime so demo data is stable.
        services.TryAddSingleton<MockMessageStore>();

        // Scoped provider — matches the lifetime of ICloudMessagingProvider registrations.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICloudMessagingProvider, InMemoryMessagingProvider>());

        return services;
    }
}
