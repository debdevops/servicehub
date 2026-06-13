using ServiceHub.Core.Enums;

namespace ServiceHub.Simulator.Store;

/// <summary>
/// Identifies a namespace registered with the simulator, carrying enough metadata
/// for the UI and status endpoints to surface provider information.
/// </summary>
/// <param name="Id">The stable namespace GUID (fixed in the seeder for deterministic tests).</param>
/// <param name="Name">The technical name of the namespace (e.g., <c>contoso-servicebus-prod</c>).</param>
/// <param name="DisplayName">A human-friendly label shown in the UI.</param>
/// <param name="Provider">The cloud provider that owns this namespace.</param>
public sealed record SimulatorNamespace(Guid Id, string Name, string DisplayName, CloudProviderType Provider);
