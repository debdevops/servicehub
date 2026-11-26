using ServiceHub.Core.Entities;
using ServiceHub.Core.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>Which components are ticked on which connection (Add a component).</summary>
public interface INamespaceComponentService
{
    /// <summary>Every stored choice for one connection.</summary>
    Task<IReadOnlyList<NamespaceComponent>> ListAsync(string ownerId, Guid namespaceId, CancellationToken cancellationToken = default);

    /// <summary>Every stored choice for the owner, across connections (the menu reads this).</summary>
    Task<IReadOnlyList<NamespaceComponent>> ListAllAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Ticks or unticks one component. Idempotent: the same call twice ends in the same state.</summary>
    Task<Result<NamespaceComponent>> SetAsync(
        string ownerId, Guid namespaceId, string componentKey, bool enabled, string changedBy, CancellationToken cancellationToken = default);
}
