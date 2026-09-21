namespace ServiceHub.Infrastructure.Routing;

/// <summary>
/// Thrown when a namespace ID cannot be resolved to an existing <see cref="ServiceHub.Core.Entities.Namespace"/>
/// (i.e. the repository lookup itself failed) — distinct from other <see cref="InvalidOperationException"/>s
/// such as provider credential/configuration errors, so only a genuine "namespace not found" maps to 404.
/// </summary>
public sealed class NamespaceResolutionException : InvalidOperationException
{
    public NamespaceResolutionException(string message) : base(message)
    {
    }
}
