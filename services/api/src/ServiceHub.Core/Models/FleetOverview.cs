using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>What ServiceHub can prove about a cloud's dead-letter queue — computed from capability, never from a name (R4).</summary>
public enum FleetCapabilityState
{
    /// <summary>Every namespace of the cloud can prove a fix held.</summary>
    CanConfirm = 0,

    /// <summary>At least one cannot without a DLQ observer.</summary>
    ObserverRequired = 1,
}

/// <summary>The three health words, defined once (card 3.5). "Waiting on you" arrives with pending work (unit 5.1).</summary>
public enum FleetHealth
{
    /// <summary>Nothing needs a look.</summary>
    Healthy = 0,

    /// <summary>Something came back after a replay, or the last connection test failed.</summary>
    NeedsALook = 1,

    /// <summary>ServiceHub does not look on its own here, so it cannot say.</summary>
    CannotTell = 2,
}

/// <summary>One cloud's own numbers. Nothing is added across clouds.</summary>
public sealed record FleetCloud(
    CloudProviderType Provider,
    int NamespaceCount,
    FleetCapabilityState Capability,
    bool Watched,
    int? Active,
    int NewInWindow,
    int ResolvedInWindow);

/// <summary>A failure reason and how many dead letters carry it.</summary>
public sealed record FleetFailure(string Reason, int Count);

/// <summary>One connected namespace.</summary>
public sealed record FleetNamespace(
    Guid Id,
    string Name,
    string? DisplayName,
    CloudProviderType Provider,
    EnvironmentType Environment,
    bool Watched,
    int? Active,
    int NewInWindow,
    int ResolvedInWindow,
    FleetFailure? TopFailure,
    FleetHealth Health);

/// <summary>What sits in dead-letter queues now: one row per cloud and reason, never one sum.</summary>
public sealed record FleetTopFailure(CloudProviderType Provider, EnvironmentType Environment, string Reason, int Count);

/// <summary>The Fleet Overview read model (unit 3.5).</summary>
public sealed record FleetOverview(
    string Window,
    DateTimeOffset Since,
    IReadOnlyList<FleetCloud> Clouds,
    IReadOnlyList<FleetNamespace> Namespaces,
    IReadOnlyList<FleetTopFailure> TopFailures);
