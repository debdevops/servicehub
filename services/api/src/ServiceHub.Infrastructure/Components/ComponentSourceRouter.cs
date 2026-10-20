using ServiceHub.Core.Components;
using ServiceHub.Core.Enums;

namespace ServiceHub.Infrastructure.Components;

/// <summary>
/// Finds a component's <see cref="IComponentSource"/> by key and adapter, like <c>CloudProviderRouter</c> does for adapters.
/// Two sources for the same component on the same adapter is a registration mistake and fails at start-up.
/// </summary>
public sealed class ComponentSourceRouter : IComponentSourceRouter
{
    private readonly IReadOnlyDictionary<(string Key, CloudProviderType Provider), IComponentSource> _sources;

    /// <summary>Creates the router from every registered source.</summary>
    public ComponentSourceRouter(IEnumerable<IComponentSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        All = [.. sources];
        var duplicates = All.GroupBy(s => (s.ComponentKey, s.Provider)).Where(g => g.Count() > 1).Select(g => $"{g.Key.ComponentKey}/{g.Key.Provider}").ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException($"More than one IComponentSource is registered for: [{string.Join(", ", duplicates)}].");
        }

        _sources = All.ToDictionary(s => (s.ComponentKey, s.Provider));
    }

    /// <inheritdoc />
    public IReadOnlyList<IComponentSource> All { get; }

    /// <inheritdoc />
    public IComponentSource? Find(string componentKey, CloudProviderType provider) =>
        _sources.TryGetValue((componentKey, provider), out var source) ? source : null;
}
