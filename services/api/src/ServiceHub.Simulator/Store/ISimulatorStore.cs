using ServiceHub.Core.Enums;

namespace ServiceHub.Simulator.Store;

/// <summary>
/// Contract for the in-memory message store used by all simulated providers.
/// Provides entity management, namespace registry, and fault injection capabilities.
/// </summary>
public interface ISimulatorStore
{
    // ── Entity management ─────────────────────────────────────────────────────

    /// <summary>Registers a new entity in the store.</summary>
    void RegisterEntity(SimulatorEntity entity, Guid namespaceId);

    /// <summary>
    /// Retrieves the entity with the given name from the specified namespace.
    /// Returns <see langword="null"/> if not found.
    /// </summary>
    SimulatorEntity? GetEntity(Guid namespaceId, string entityName);

    /// <summary>Returns all entities registered under the specified namespace.</summary>
    IReadOnlyList<SimulatorEntity> GetEntities(Guid namespaceId);

    // ── Namespace management ──────────────────────────────────────────────────

    /// <summary>Registers a namespace in the store.</summary>
    void RegisterNamespace(Guid namespaceId, string name, string displayName, CloudProviderType provider);

    /// <summary>Returns all registered namespaces.</summary>
    IReadOnlyList<SimulatorNamespace> GetAllNamespaces();

    /// <summary>Returns the namespace with the given ID, or <see langword="null"/> if not found.</summary>
    SimulatorNamespace? GetNamespace(Guid namespaceId);

    // ── Fault injection ───────────────────────────────────────────────────────

    /// <summary>Injects a fault that affects the targeted entity during peek/send operations.</summary>
    void InjectFault(SimulatorFault fault);

    /// <summary>Returns all faults whose <c>ExpiresAt</c> is in the future.</summary>
    IReadOnlyList<SimulatorFault> GetActiveFaults();

    /// <summary>
    /// Returns active faults whose <c>TargetEntity</c> matches <paramref name="entityName"/>
    /// and whose <c>NamespaceId</c> matches <paramref name="namespaceId"/>.
    /// </summary>
    IReadOnlyList<SimulatorFault> GetActiveFaultsFor(Guid namespaceId, string entityName);

    /// <summary>Removes all injected faults (expired and active).</summary>
    void ClearFaults();

    // ── State reset ───────────────────────────────────────────────────────────

    /// <summary>Wipes all message state from every entity but preserves entity registrations.</summary>
    void Reset();

    /// <summary>
    /// Full teardown: removes all entities, namespaces, and faults.
    /// Called before re-seeding so the store is in a pristine state.
    /// </summary>
    void Purge();
}
