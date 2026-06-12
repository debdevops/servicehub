using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Routing;

/// <summary>
/// Resolves the correct <see cref="ICloudMessagingProvider"/> implementation for a given
/// <see cref="CloudProviderType"/> at runtime.
/// Providers are registered by calling <c>AddAzureProvider()</c> (and future provider
/// registration methods) during DI setup.  If a requested provider has not been registered
/// an <see cref="InvalidOperationException"/> is thrown with a clear diagnostic message.
/// </summary>
public sealed class CloudProviderRouter
{
    private readonly IReadOnlyDictionary<CloudProviderType, ICloudMessagingProvider> _providers;

    /// <summary>
    /// Initialises a new instance of <see cref="CloudProviderRouter"/> with all registered providers.
    /// </summary>
    /// <param name="providers">
    /// All <see cref="ICloudMessagingProvider"/> implementations registered with the DI container.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="providers"/> is null.</exception>
    public CloudProviderRouter(IEnumerable<ICloudMessagingProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var providerList = providers.ToList();
        var duplicates = providerList
            .GroupBy(p => p.ProviderType)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
            throw new InvalidOperationException(
                $"Multiple ICloudMessagingProvider implementations are registered for the same ProviderType: " +
                $"[{string.Join(", ", duplicates)}]. Each ProviderType must have exactly one provider registered.");
        _providers = providerList.ToDictionary(p => p.ProviderType);
    }

    /// <summary>
    /// Returns the <see cref="ICloudMessagingProvider"/> registered for the given
    /// <paramref name="providerType"/>.
    /// </summary>
    /// <param name="providerType">The cloud provider to resolve.</param>
    /// <returns>The registered provider implementation.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no provider implementation has been registered for
    /// <paramref name="providerType"/>.  This is expected for AWS and GCP in Phase 1 —
    /// their providers will be added in subsequent phases.
    /// </exception>
    public ICloudMessagingProvider Resolve(CloudProviderType providerType)
    {
        if (_providers.TryGetValue(providerType, out var provider))
        {
            return provider;
        }

        throw new InvalidOperationException(
            $"No ICloudMessagingProvider has been registered for cloud provider '{providerType}'. " +
            $"Registered providers: [{string.Join(", ", _providers.Keys)}]. " +
            $"Add the required provider package and call the corresponding registration method in DependencyInjection.");
    }

    /// <summary>
    /// Returns <see langword="true"/> if a provider has been registered for the given type.
    /// Useful for feature-flag checks without triggering an exception.
    /// </summary>
    /// <param name="providerType">The cloud provider to check.</param>
    public bool IsRegistered(CloudProviderType providerType) =>
        _providers.ContainsKey(providerType);
}
