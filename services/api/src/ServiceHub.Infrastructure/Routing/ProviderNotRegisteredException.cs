namespace ServiceHub.Infrastructure.Routing;

/// <summary>
/// Thrown when a namespace references a cloud provider with no registered
/// <see cref="ServiceHub.Core.Interfaces.ICloudMessagingProvider"/> implementation
/// (e.g. the provider's feature flag is disabled) — distinct from the namespace
/// itself not existing, so callers can map the two cases to different HTTP statuses.
/// </summary>
public sealed class ProviderNotRegisteredException : InvalidOperationException
{
    public ProviderNotRegisteredException(string message) : base(message)
    {
    }
}
