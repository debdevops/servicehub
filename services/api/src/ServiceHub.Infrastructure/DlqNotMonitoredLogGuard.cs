using System.Collections.Concurrent;
using ServiceHub.Core.Enums;

namespace ServiceHub.Infrastructure;

/// <summary>
/// Tracks which providers have already logged a "DLQ scanning skipped" warning, so the message
/// appears once per process instead of once per poll interval. Registered as a singleton because
/// <see cref="DlqMonitorService"/> is Scoped and recreated for every namespace scan — an instance
/// field on the service itself would not survive across scans.
/// </summary>
public sealed class DlqNotMonitoredLogGuard
{
    private readonly ConcurrentDictionary<CloudProviderType, byte> _logged = new();

    /// <summary>
    /// Returns true the first time it is called for <paramref name="provider"/>, and false on
    /// every subsequent call for that same provider.
    /// </summary>
    public bool ShouldLog(CloudProviderType provider) => _logged.TryAdd(provider, 0);
}
