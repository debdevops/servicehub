namespace ServiceHub.Simulator.Store;

/// <summary>
/// Describes a transient fault to inject into the simulated environment.
/// Simulated receivers consult the active fault list before returning messages,
/// and return appropriate errors when a matching fault is active.
/// </summary>
/// <param name="FaultType">
/// Category of fault:
/// <list type="bullet">
///   <item><c>MaxDelivery</c> — force messages to exceed max delivery count</item>
///   <item><c>VisibilityExpiry</c> — expire AWS visibility windows immediately</item>
///   <item><c>AckDeadlineStorm</c> — set all GCP ack deadlines to 1 second</item>
///   <item><c>KmsError</c> — simulate AWS KMS key inaccessible</item>
///   <item><c>OrderingStall</c> — stall GCP ordering-key message delivery</item>
///   <item><c>NetworkTimeout</c> — return a network-timeout error on peek</item>
/// </list>
/// </param>
/// <param name="TargetEntity">Name of the entity (queue / subscription) to affect. Empty string means all entities.</param>
/// <param name="NamespaceId">ID of the namespace whose entity is affected.</param>
/// <param name="Severity">1–10 — controls how many messages the fault affects.</param>
/// <param name="ExpiresAt">Wall-clock UTC time when the fault automatically expires.</param>
public sealed record SimulatorFault(
    string FaultType,
    string TargetEntity,
    Guid NamespaceId,
    int Severity,
    DateTimeOffset ExpiresAt);
