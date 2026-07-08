namespace ServiceHub.Core.Events;

/// <summary>
/// CloudEvents-inspired envelope that wraps every internal platform event published
/// through <see cref="ServiceHub.Core.Interfaces.IPlatformEventBus"/>.
/// <para>
/// Design notes:
/// <list type="bullet">
///   <item>
///     The envelope carries context fields (who, where, when, severity) separately
///     from the typed payload, so consumers can route and filter without
///     deserialising the payload.
///   </item>
///   <item>
///     <see cref="Payload"/> is <c>object?</c> — the bus is payload-agnostic.
///     Publishers set a concrete payload record (e.g. <c>NamespaceCreatedPayload</c>);
///     subscribers cast using <see cref="EventType"/> as the discriminator.
///   </item>
///   <item>
///     <see cref="CloudProvider"/> is <c>string?</c> rather than an enum so that
///     future providers (Kafka, IBM MQ, RabbitMQ) do not require a Core assembly change.
///   </item>
///   <item>
///     This record is immutable by design. All properties use <c>init</c> accessors.
///   </item>
/// </list>
/// </para>
/// </summary>
public sealed record PlatformEvent
{
    /// <summary>
    /// Unique identifier for this event instance.
    /// Defaults to a new <see cref="Guid"/> on construction.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Schema version of the <see cref="PlatformEvent"/> envelope itself.
    /// Increment only when the envelope structure changes.
    /// Payload versioning is expressed in <see cref="EventType"/>.
    /// </summary>
    public int Version { get; init; } = 1;

    /// <summary>
    /// UTC timestamp when the domain fact occurred.
    /// Set by the publisher immediately after the state-change is committed.
    /// </summary>
    public DateTimeOffset OccurredUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Origin of the event within ServiceHub.
    /// Recommended format: <c>{assembly}.{class}</c>,
    /// e.g. <c>ServiceHub.Infrastructure.DlqMonitorWorker</c>.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Top-level domain grouping.
    /// Use constants from <see cref="EventCategories"/>
    /// (e.g. <c>EventCategories.Dlq</c>).
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// Canonical dotted-name identifier for this event type.
    /// Use constants from <see cref="EventTypes"/>
    /// (e.g. <c>EventTypes.DlqSpikeDetected</c>).
    /// This is the primary discriminator used by subscribers to identify payload type.
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>
    /// Operational significance of this event.
    /// </summary>
    public EventSeverity Severity { get; init; } = EventSeverity.Info;

    /// <summary>
    /// Cloud provider identifier for the namespace involved in this event.
    /// Intentionally a plain string (not an enum) to remain forward-compatible
    /// with providers not yet defined in <c>CloudProviderType</c>.
    /// Expected values: <c>"azure"</c>, <c>"aws"</c>, <c>"gcp"</c>, <c>"simulator"</c>.
    /// Null when the event is not namespace-scoped.
    /// </summary>
    public string? CloudProvider { get; init; }

    /// <summary>
    /// Identifier of the namespace involved in this event.
    /// Null for events that are not scoped to a specific namespace.
    /// </summary>
    public Guid? NamespaceId { get; init; }

    /// <summary>
    /// Display name of the namespace involved in this event.
    /// Snapshotted at publish time so that consumers have a human-readable label
    /// even if the namespace is later deleted.
    /// </summary>
    public string? NamespaceName { get; init; }

    /// <summary>
    /// Optional correlation identifier propagated from the originating HTTP request
    /// or background job cycle, enabling cross-component tracing.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Identity of the actor that triggered the event.
    /// Matches the <c>UserIdentity</c> format used by <c>AuditLog</c>:
    /// user email, API key name, or <c>"System:&lt;RuleName&gt;"</c> for automation.
    /// </summary>
    public string? Actor { get; init; }

    /// <summary>
    /// Logical scope or resource path within the namespace that the event targets.
    /// Examples: queue name, topic/subscription path, rule ID.
    /// </summary>
    public string? TargetScope { get; init; }

    /// <summary>
    /// Optional key-value pairs carrying additional context that does not belong
    /// in the typed payload. Must never contain secrets or raw message bodies.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// The typed payload record for this event.
    /// Subscribers should use <see cref="EventType"/> as the discriminator before casting.
    /// May be null for events that carry no additional data beyond the envelope fields.
    /// </summary>
    public object? Payload { get; init; }
}
