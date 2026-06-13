using System.Collections.Concurrent;
using ServiceHub.Core.Enums;

namespace ServiceHub.Simulator.Store;

/// <summary>
/// Thread-safe, fully in-memory implementation of <see cref="ISimulatorStore"/>.
/// All dictionaries use <see cref="ConcurrentDictionary{TKey,TValue}"/> for lock-free reads.
/// Write operations that span multiple collections are protected by a dedicated lock.
/// </summary>
public sealed class InMemorySimulatorStore : ISimulatorStore
{
    // Key: namespaceId → (entityName → entity)
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, SimulatorEntity>> _entities = new();
    private readonly ConcurrentDictionary<Guid, SimulatorNamespace> _namespaces = new();
    private readonly ConcurrentBag<SimulatorFault> _faults = new();
    private readonly Lock _writeLock = new();

    // ── Entity management ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void RegisterEntity(SimulatorEntity entity, Guid namespaceId)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var bucket = _entities.GetOrAdd(namespaceId, _ => new ConcurrentDictionary<string, SimulatorEntity>(StringComparer.OrdinalIgnoreCase));
        bucket[entity.Name] = entity;
    }

    /// <inheritdoc/>
    public SimulatorEntity? GetEntity(Guid namespaceId, string entityName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        if (_entities.TryGetValue(namespaceId, out var bucket) &&
            bucket.TryGetValue(entityName, out var entity))
            return entity;
        return null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<SimulatorEntity> GetEntities(Guid namespaceId) =>
        _entities.TryGetValue(namespaceId, out var bucket)
            ? bucket.Values.ToList()
            : [];

    // ── Namespace management ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public void RegisterNamespace(Guid namespaceId, string name, string displayName, CloudProviderType provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _namespaces[namespaceId] = new SimulatorNamespace(namespaceId, name, displayName, provider);
    }

    /// <inheritdoc/>
    public IReadOnlyList<SimulatorNamespace> GetAllNamespaces() => _namespaces.Values.ToList();

    /// <inheritdoc/>
    public SimulatorNamespace? GetNamespace(Guid namespaceId) =>
        _namespaces.TryGetValue(namespaceId, out var ns) ? ns : null;

    // ── Fault injection ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void InjectFault(SimulatorFault fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        _faults.Add(fault);
    }

    /// <inheritdoc/>
    public IReadOnlyList<SimulatorFault> GetActiveFaults()
    {
        var now = DateTimeOffset.UtcNow;
        return [.. _faults.Where(f => f.ExpiresAt > now)];
    }

    /// <inheritdoc/>
    public IReadOnlyList<SimulatorFault> GetActiveFaultsFor(Guid namespaceId, string entityName)
    {
        var now = DateTimeOffset.UtcNow;
        return [.. _faults.Where(f =>
            f.ExpiresAt > now &&
            f.NamespaceId == namespaceId &&
            (string.IsNullOrEmpty(f.TargetEntity) ||
             string.Equals(f.TargetEntity, entityName, StringComparison.OrdinalIgnoreCase)))];
    }

    /// <inheritdoc/>
    public void ClearFaults()
    {
        while (_faults.TryTake(out _)) { }
    }

    // ── State reset ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Reset()
    {
        foreach (var bucket in _entities.Values)
            foreach (var entity in bucket.Values)
                entity.Clear();
    }

    /// <inheritdoc/>
    public void Purge()
    {
        lock (_writeLock)
        {
            _entities.Clear();
            _namespaces.Clear();
            ClearFaults();
        }
    }
}
