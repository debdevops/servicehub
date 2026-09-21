using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Resolves the correct <see cref="ICloudMessagingProvider"/> implementation for a given
/// <see cref="CloudProviderType"/> at runtime. The Api layer depends on this interface, not
/// the concrete <c>ServiceHub.Infrastructure.Routing.CloudProviderRouter</c> — Controllers
/// depend on Core interfaces, not concrete Infrastructure types.
/// </summary>
public interface ICloudProviderRouter
{
    /// <summary>
    /// Returns the <see cref="ICloudMessagingProvider"/> registered for the given
    /// <paramref name="providerType"/>.
    /// </summary>
    /// <param name="providerType">The cloud provider to resolve.</param>
    /// <returns>The registered provider implementation.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no provider implementation has been registered for <paramref name="providerType"/>.
    /// </exception>
    ICloudMessagingProvider Resolve(CloudProviderType providerType);

    /// <summary>
    /// Returns <see langword="true"/> if a provider has been registered for the given type.
    /// Useful for feature-flag checks without triggering an exception.
    /// </summary>
    /// <param name="providerType">The cloud provider to check.</param>
    bool IsRegistered(CloudProviderType providerType);
}
